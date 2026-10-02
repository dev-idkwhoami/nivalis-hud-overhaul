using HarmonyLib;
using BepInEx.Unity.IL2CPP;
using Nivalis;
using Nivalis.UI;

namespace NivalisMods.HudOverhaul;

internal static class QuestPatchRegistration
{
    internal static bool Installed { get; private set; }
    internal static string[] BlockingMods { get; private set; } = Array.Empty<string>();
    internal static string UnavailableText => BlockingMods.Length > 0
        ? Labels.Get(BlockingMods.Length == 1 ? "settings.quests.unavailable" : "settings.quests.unavailableMultiple")
            .Replace("{mods}", string.Join(", ", BlockingMods))
        : Labels.Get("settings.quests.unavailableOther");

    private static readonly HashSet<Type> QuestPatches = new()
    {
        typeof(QuestPinChangedPatch), typeof(PinnedHudQuestsPatch), typeof(PinnedCompassQuestsPatch)
    };

    internal static void Install(Harmony harmony)
    {
        // The soft dependency loads the known provider first, independent of
        // DLL filename order. Do not even create Harmony processors for our
        // quest patches when that provider is present.
        var plugins = IL2CPPChainloader.Instance.Plugins.ToDictionary(p => p.Key, p => p.Value.Metadata.Name);
        var knownProvider = QuestModCompatibility.Providers.Any(p => plugins.ContainsKey(p.Id));
        var compass = AccessTools.Method(typeof(NavigationUI), nameof(NavigationUI.LateUpdate));
        var hud = AccessTools.Method(typeof(ActiveJournalEntriesUi), nameof(ActiveJournalEntriesUi.Refresh));
        var compassCheck = new QuestFilterBackoff(compass, Plugin.Id, message => Plugin.Logger.LogInfo(message));
        var hudCheck = new QuestFilterBackoff(hud, Plugin.Id, message => Plugin.Logger.LogInfo(message));
        var compassOwned = compassCheck.ShouldBackOff();
        var hudOwned = hudCheck.ShouldBackOff();
        Installed = !knownProvider && !compassOwned && !hudOwned;
        var owners = compassCheck.OtherOwners.Concat(hudCheck.OtherOwners);
        BlockingMods = QuestModCompatibility.BlockingNames(plugins, owners);

        // Same class processing as Harmony.PatchAll, excluding the entire
        // quest-filter module rather than installing callbacks that return early.
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            if (!Installed && QuestPatches.Contains(type)) continue;
            harmony.CreateClassProcessor(type).Patch();
        }

        Plugin.Logger.LogInfo(Installed
            ? "Quest filter module installed; runtime conflict checks remain active."
            : $"Quest filter module not installed: {(BlockingMods.Length > 0 ? string.Join(", ", BlockingMods) : "another quest display patch or unavailable compatibility information")}. Other HUD Overhaul features remain active.");
    }
}
