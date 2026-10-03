using System.Text.Json;
using HarmonyLib;
using Nivalis.UI;

namespace NivalisMods.HudOverhaul;

internal static class QuestHudVisibility
{
    private static string _path = null!;
    private static bool? _hidden;

    internal static void Initialize(string path)
    {
        _path = path;
        _hidden = null;
        if (File.Exists(path)) _hidden = JsonSerializer.Deserialize<bool>(File.ReadAllText(path));
    }

    internal static void Restore(ActiveJournalEntriesUi hud)
    {
        if (_hidden is not bool hidden || hud._hidden == hidden) return;
        hud._hidden = hidden;
        if (hidden) hud.HideAnimated();
        else hud.ShowAnimated();
    }

    internal static void Remember(ActiveJournalEntriesUi hud)
    {
        if (_hidden == hud._hidden) return;
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(hud._hidden));
        File.Move(temporary, _path, true);
        _hidden = hud._hidden;
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
