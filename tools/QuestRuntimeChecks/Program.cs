using System.Reflection;
using Nivalis;
using Nivalis.Navigation;
using Nivalis.UI;
using NivalisMods.HudOverhaul;
using MarkerList = Il2CppSystem.Collections.Generic.List<Nivalis.UI.CompassMarkerData>;

var checks = 0;
void Check(string name, bool value)
{
    if (!value) throw new Exception(name);
    checks++;
}
T Callback<T>(Type type, string method) where T : Delegate =>
    type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<T>();
var begin = Callback<BeginDraw>(typeof(PinnedCompassQuestsPatch), "Prefix");
var end = Callback<Action<PinnedCompassQuestsPatch.DisplayScope>>(typeof(PinnedCompassQuestsPatch), "Finalizer");
var registryBegin = Callback<BeginChange>(typeof(CompassRegistryPatch), "Prefix");
var registryEnd = Callback<Action<NavigationManager, MarkerList?>>(typeof(CompassRegistryPatch), "Finalizer");

if (args.Any(a => a.EndsWith("-startup")))
{
    var chain = BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance;
    QuestPatchRegistration.Install(new HarmonyLib.Harmony(Plugin.Id));
    var startupComponent = new QuestStartup(IntPtr.Zero);
    startupComponent.Update();
    Check("plugin Load does not make a premature compatibility decision", CompanionSettings.AvailabilityUpdates == 0);
    var questTypes = new[] { typeof(QuestManagerLifecyclePatch), typeof(QuestSaveLifecyclePatch), typeof(PinnedHudQuestsPatch), typeof(PinnedCompassQuestsPatch), typeof(CompassRegistryPatch), typeof(QuestHudVisibilityPatch) };
    Check("non-quest features install immediately", HarmonyLib.Harmony.Installed.ContainsKey(typeof(NonQuestSentinel)));
    Check("no quest patch processor is created during plugin Load", !questTypes.Any(HarmonyLib.Harmony.Processed.Contains));
    var allowed = args.Contains("--allowed-startup");
    if (args.Contains("--blocked-startup"))
        chain.Plugins.Add(QuestModCompatibility.TrackedHudId, new());
    if (args.Contains("--foreign-startup"))
        HarmonyLib.Harmony.Metadata.Add(typeof(NavigationUI).GetMethod(nameof(NavigationUI.LateUpdate))!, new("later.plugin"));
    chain.Finish();
    Check("Finished only schedules registration after startup handlers", CompanionSettings.AvailabilityUpdates == 0);
    startupComponent.Update();
    Check("startup checks both display owners exactly once", HarmonyLib.Harmony.Inspections == 2);
    Check("module installation matches final compatibility decision", QuestPatchRegistration.Installed == allowed);
    Check("blocked modules never create quest processors", questTypes.All(type => HarmonyLib.Harmony.Processed.Contains(type) == allowed));
    Check("all quest hooks are installed together or entirely absent", questTypes.All(type => HarmonyLib.Harmony.Installed.ContainsKey(type) == allowed));
    if (args.Contains("--blocked-startup"))
        Check("known provider is identified", QuestPatchRegistration.BlockingMods.SequenceEqual(new[] { "Tracked Quests HUD" }));
    Check("startup component stops after the one decision", !startupComponent.enabled && CompanionSettings.AvailabilityUpdates == 1);
    chain.Plugins.Clear(); chain.Finish(); startupComponent.Update();
    Check("startup decision cannot change during gameplay", QuestPatchRegistration.Installed == allowed && CompanionSettings.AvailabilityUpdates == 1 && HarmonyLib.Harmony.Inspections == 2);
    Console.WriteLine($"Passed {checks} deferred registration checks (production installer, recording patcher).");
    return;
}

var visibilityPath = Path.Combine(Path.GetTempPath(), "hud-visibility-" + Guid.NewGuid() + ".json");
var restoreVisibility = Callback<Action<ActiveJournalEntriesUi>>(typeof(QuestHudVisibilityPatch), "Started");
var saveVisibility = Callback<Action<ActiveJournalEntriesUi>>(typeof(QuestHudVisibilityPatch), "Toggled");
try
{
    QuestHudVisibility.Initialize(visibilityPath);
    var hud = new ActiveJournalEntriesUi();
    restoreVisibility(hud);
    Check("first launch retains native visibility without writing a preference", !hud._hidden && hud.Hides == 0 && !File.Exists(visibilityPath));
    hud.OnToggleQuestHUDDIsplayPreformed();
    saveVisibility(hud);
    QuestHudVisibility.Initialize(visibilityPath);
    var reloadedHud = new ActiveJournalEntriesUi();
    restoreVisibility(reloadedHud);
    Check("hidden preference survives restart and restores the native animation", reloadedHud._hidden && reloadedHud.Hides == 1);
    restoreVisibility(reloadedHud);
    Check("restoring an unchanged preference does not restart animation", reloadedHud.Hides == 1);
    reloadedHud.OnToggleQuestHUDDIsplayPreformed();
    saveVisibility(reloadedHud);
    QuestHudVisibility.Initialize(visibilityPath);
    var hiddenHud = new ActiveJournalEntriesUi { _hidden = true };
    restoreVisibility(hiddenHud);
    Check("visible preference survives restart too", !hiddenHud._hidden && hiddenHud.Shows == 1);
    File.WriteAllText(visibilityPath, "invalid");
    try { QuestHudVisibility.Initialize(visibilityPath); }
    catch (System.Text.Json.JsonException) { }
    var unchangedHud = new ActiveJournalEntriesUi { _hidden = true };
    restoreVisibility(unchangedHud);
    Check("invalid preference retains native state", unchangedHud._hidden && unchangedHud.Shows == 0);
    saveVisibility(unchangedHud);
    Check("next player toggle can replace an invalid preference", File.ReadAllText(visibilityPath) == "true");
}
finally
{
    File.Delete(visibilityPath);
    File.Delete(visibilityPath + ".tmp");
}

var startup = new QuestStartupGate();
for (var i = 0; i < 100; i++) Check("no registration before all plugins load", !startup.TryBegin());
startup.PluginsLoaded();
Check("registration begins on first update after startup", startup.TryBegin());
startup.PluginsLoaded();
Check("repeated callbacks cannot trigger a second compatibility check", !startup.TryBegin());

var quests = QuestManager._instance = new QuestManager();
var pinned = new RuntimeQuest { Pinned = true };
var unpinned = new RuntimeQuest();
quests._activeQuests.Entries.AddRange([pinned, unpinned]);
var nav = NavigationManager.Instance = new NavigationManager();
var source = nav._markers;
var ordinary = new CompassMarkerData();
var wanted = new CompassMarkerData { Quest = pinned.Quest };
var hidden = new CompassMarkerData { Quest = unpinned.Quest };
source.Add(ordinary); source.Add(wanted); source.Add(hidden);
begin(out var scope);
var view = nav._markers;
Check("active tracking supplies a secondary view", view != source && view.Count == 2 && view.Contains(wanted) && view.Contains(ordinary));
end(scope);
Check("draw restores the complete original registry", nav._markers == source && source.Count == 3);
var reads = quests._activeQuests.Reads;
var markerReads = source.Reads;
var viewVersion = view._version;
var allocated = GC.GetAllocatedBytesForCurrentThread();
for (var frame = 0; frame < 10000; frame++) { begin(out scope); end(scope); }
Check("unchanged frames allocate no managed garbage in callbacks", GC.GetAllocatedBytesForCurrentThread() == allocated);
Check("unchanged frames do not enumerate quests or markers", reads == quests._activeQuests.Reads && markerReads == source.Reads && view._version == viewVersion);
Check("subscriptions are installed only once", quests.OnQuestsPinnedChanged.Count == 1 && quests.OnQuestsUpdated.Count == 1);
wanted.Position = 200;
begin(out scope);
Check("moving targets update through the same marker object", nav._markers == view && nav._markers[1].Position == 200 && source.Reads == markerReads);
end(scope);
quests.OnQuestsUpdated.Invoke();
begin(out scope); end(scope);
Check("progress without a changed pin selection does not rebuild markers", view._version == viewVersion && source.Reads == markerReads);

unpinned.Pinned = true;
quests.OnQuestsPinnedChanged.Invoke();
begin(out scope);
Check("pin notification includes the new quest immediately", nav._markers.Count == 3 && nav._markers.Contains(hidden));
end(scope);
pinned.IsActive = false;
quests.OnQuestCompleted.Invoke(pinned.Quest);
begin(out scope);
Check("completion notification removes completed quest markers", nav._markers.Count == 2 && !nav._markers.Contains(wanted));
end(scope);
unpinned.Pinned = false;
quests.OnQuestsPinnedChanged.Invoke();
begin(out scope);
Check("last unpin bypasses the proxy and restores native selection", nav._markers == source && scope.Manager == null);
end(scope);
unpinned.Pinned = true;
quests.OnQuestsPinnedChanged.Invoke();
ModOptions.Quests.Value = false;
QuestTracking.Invalidate();
begin(out scope);
Check("disabled feature uses original list", nav._markers == source && scope.Manager == null);
end(scope);
ModOptions.Quests.Value = true;
QuestTracking.Invalidate();
begin(out scope); end(scope);

var added = new CompassMarkerData { Quest = unpinned.Quest };
source.Add(added);
begin(out scope);
Check("marker registration invalidates cached selection", nav._markers.Contains(added));
end(scope);
source.Remove(added);
var replacement = new CompassMarkerData();
source.Add(replacement);
begin(out scope);
Check("same-count marker replacement is detected by list version", !nav._markers.Contains(added) && nav._markers.Contains(replacement));
end(scope);

// A native enable/disable callback during compass drawing must update the
// registry rather than the temporarily selected view, even on an exception.
begin(out scope);
var drawingView = nav._markers;
registryBegin(nav, out var registryScope);
var duringDraw = new CompassMarkerData();
nav.RegisterCompassMarker(duringDraw);
registryEnd(nav, registryScope);
Check("reentrant marker registration keeps display view selected", nav._markers == drawingView && source.Contains(duringDraw) && !drawingView.Contains(duringDraw));
try { throw new InvalidOperationException("native draw failed"); }
catch (InvalidOperationException) { }
finally { end(scope); }
Check("exception cleanup restores registry including new marker", nav._markers == source && source.Contains(duringDraw));
begin(out scope);
Check("next draw includes marker registered during previous draw", nav._markers.Contains(duringDraw));
end(scope);

source = nav._markers = new MarkerList();
source.Add(wanted); source.Add(ordinary);
begin(out scope);
Check("replacement registry cannot reuse stale source", nav._markers.Count == 1 && nav._markers.Contains(ordinary));
end(scope);
var oldQuests = quests;
QuestTracking.Reset();
Check("save reset removes all old event subscriptions", oldQuests.OnQuestsPinnedChanged.Count == 0 && oldQuests.OnQuestsUpdated.Count == 0 && oldQuests.OnQuestStarted.Count == 0 && oldQuests.OnQuestCompleted.Count == 0);
quests = QuestManager._instance = new QuestManager();
var newPinned = new RuntimeQuest { Pinned = true };
quests._activeQuests.Entries.Add(newPinned);
nav = NavigationManager.Instance = new NavigationManager();
source = nav._markers;
var newMarker = new CompassMarkerData { Quest = newPinned.Quest };
source.Add(newMarker); source.Add(wanted);
begin(out scope);
Check("new save and manager discard previous selection and markers", nav._markers.Count == 1 && nav._markers.Contains(newMarker));
end(scope);
var newlyStarted = new RuntimeQuest { Pinned = true };
quests._activeQuests.Entries.Add(newlyStarted);
quests.OnQuestStarted.Invoke(newlyStarted.Quest);
Check("quest start notification refreshes selection", QuestTracking.Capture().ShowQuest(newlyStarted.Quest.Pointer.ToInt64()));
QuestTracking.Reset(); QuestManager._instance = null;
Check("unloaded gameplay has no retained quest pins", !QuestTracking.Capture().HasPins);
Console.WriteLine($"Passed {checks} quest lifecycle and cached compass checks (managed native stand-ins).");

delegate void BeginDraw(out PinnedCompassQuestsPatch.DisplayScope scope);
delegate void BeginChange(NavigationManager manager, out MarkerList? scope);
