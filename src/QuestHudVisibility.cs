using HarmonyLib;
using Nivalis.UI;

namespace NivalisMods.HudOverhaul;

internal static class QuestHudVisibility
{
    internal static void Restore(ActiveJournalEntriesUi hud)
    {
        var hidden = !ModOptions.QuestHudVisible.Value;
        if (hud._hidden == hidden) return;
        hud._hidden = hidden;
        if (hidden) hud.HideAnimated();
        else hud.ShowAnimated();
    }

    internal static void Remember(ActiveJournalEntriesUi hud)
    {
        ModOptions.QuestHudVisible.Value = !hud._hidden;
    }

    internal static void ApplySetting()
    {
        if (!QuestPatchRegistration.Installed) return;
        foreach (var hud in UnityEngine.Object.FindObjectsOfType<ActiveJournalEntriesUi>()) Restore(hud);
    }
}

// Save only an explicit native toggle, never temporary hiding by menus or scenes.
// Both hooks belong to the deferred quest module and yield to conflicting mods.
[HarmonyPatch(typeof(ActiveJournalEntriesUi))]
internal static class QuestHudVisibilityPatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(ActiveJournalEntriesUi.Start))]
    private static void Started(ActiveJournalEntriesUi __instance) =>
        Plugin.Guard("Restore quest HUD visibility", () => QuestHudVisibility.Restore(__instance));

    [HarmonyPostfix, HarmonyPatch(nameof(ActiveJournalEntriesUi.OnToggleQuestHUDDIsplayPreformed))]
    private static void Toggled(ActiveJournalEntriesUi __instance) =>
        Plugin.Guard("Save quest HUD visibility", () => QuestHudVisibility.Remember(__instance));
}
