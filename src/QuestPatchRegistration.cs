using HarmonyLib;
using BepInEx.Unity.IL2CPP;
using Nivalis;
using Nivalis.UI;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class QuestPatchRegistration
{
    internal static readonly QuestStartupGate Startup = new();
    internal static bool Installed { get; private set; }
    internal static string[] BlockingMods { get; private set; } = Array.Empty<string>();
    internal static string UnavailableText => BlockingMods.Length > 0
        ? Labels.Get(BlockingMods.Length == 1 ? "settings.quests.unavailable" : "settings.quests.unavailableMultiple")
            .Replace("{mods}", string.Join(", ", BlockingMods))
        : Labels.Get("settings.quests.unavailableOther");

    private static readonly HashSet<Type> QuestPatches = new()
    {
        typeof(QuestManagerLifecyclePatch), typeof(QuestSaveLifecyclePatch),
        typeof(PinnedHudQuestsPatch), typeof(PinnedCompassQuestsPatch), typeof(CompassRegistryPatch),
        typeof(QuestHudVisibilityPatch)
    };

    internal static void Install(Harmony harmony)
    {
        // Other HUD features can install now. Not one quest/compass hook is
        // processed until every plugin's Load and Finished handlers have run.
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            if (!QuestPatches.Contains(type)) harmony.CreateClassProcessor(type).Patch();
        IL2CPPChainloader.Instance.Finished += PluginsLoaded;
    }

    private static void PluginsLoaded()
    {
        IL2CPPChainloader.Instance.Finished -= PluginsLoaded;
        Startup.PluginsLoaded();
    }

    internal static void CompleteStartup()
    {
        var plugins = IL2CPPChainloader.Instance.Plugins.ToDictionary(p => p.Key, p => p.Value.Metadata.Name);
        var knownProvider = QuestModCompatibility.Providers.Any(p => plugins.ContainsKey(p.Id));
        var compass = AccessTools.Method(typeof(NavigationUI), nameof(NavigationUI.LateUpdate));
        var hud = AccessTools.Method(typeof(ActiveJournalEntriesUi), nameof(ActiveJournalEntriesUi.Refresh));
        var compassCheck = new QuestFilterBackoff(compass, Plugin.Id, message => Plugin.Logger.LogInfo(message));
        var hudCheck = new QuestFilterBackoff(hud, Plugin.Id, message => Plugin.Logger.LogInfo(message));
        var compassOwned = compassCheck.ShouldBackOff();
        var hudOwned = hudCheck.ShouldBackOff();
        var owners = compassCheck.OtherOwners.Concat(hudCheck.OtherOwners);
        BlockingMods = QuestModCompatibility.BlockingNames(plugins, owners);
        if (!knownProvider && !compassOwned && !hudOwned)
        {
            // Separate owner allows rollback of a partial install without
            // removing unrelated HUD features.
            var quests = new Harmony(Plugin.Id + ".quests");
            try
            {
                foreach (var type in QuestPatches) quests.CreateClassProcessor(type).Patch();
                Installed = true;
            }
            catch
            {
                quests.UnpatchSelf();
                throw;
            }
        }
        CompanionSettings.SetQuestAvailability();
        Plugin.Logger.LogInfo(Installed
            ? "Quest filter module installed after plugin startup; compatibility checked once."
            : $"Quest filter module not installed: {(BlockingMods.Length > 0 ? string.Join(", ", BlockingMods) : "another quest display patch or unavailable compatibility information")}. Other HUD Overhaul features remain active.");
    }
}

public sealed class QuestStartup : MonoBehaviour
{
    public QuestStartup(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        if (!QuestPatchRegistration.Startup.TryBegin()) return;
        enabled = false;
        Plugin.Guard("Complete quest compatibility check", QuestPatchRegistration.CompleteStartup);
    }
}
