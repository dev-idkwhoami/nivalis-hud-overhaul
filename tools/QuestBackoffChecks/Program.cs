using System.Reflection;
using HarmonyLib;
using NivalisMods.HudOverhaul;

// Exercise the shipped Harmony metadata without native detours. Live IL2CPP
// coexistence still requires a game test.
const string ownId = "backoff.checks.own", otherId = "backoff.checks.other";
var compass = typeof(Targets).GetMethod(nameof(Targets.Compass))!;
var hud = typeof(Targets).GetMethod(nameof(Targets.Hud))!;
var method = new HarmonyMethod(typeof(Targets).GetMethod(nameof(Targets.Noop))!);
var ours = new Patch(method, 0, ownId);
var theirs = new Patch(method, 1, otherId);
var registry = new Dictionary<MethodBase, Patches>();
Patches? Inspect(MethodBase target) => registry.GetValueOrDefault(target);
var messages = new List<string>();
var guard = new QuestFilterBackoff(compass, ownId, messages.Add, Inspect);
var hudGuard = new QuestFilterBackoff(hud, ownId, messages.Add, Inspect);
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}

Check(!guard.ShouldBackOff(), "No patches should allow filtering");
registry[compass] = new Patches([ours], [], [], [ours], []);
Check(!guard.ShouldBackOff(), "Our own patches should allow filtering");
foreach (var kind in new[] { "prefix", "postfix", "finalizer", "transpiler", "ilmanipulator" })
{
    registry[compass] = new Patches(kind == "prefix" ? [ours, theirs] : [ours],
        kind == "postfix" ? [theirs] : [], kind == "transpiler" ? [theirs] : [],
        kind == "finalizer" ? [ours, theirs] : [ours], kind == "ilmanipulator" ? [theirs] : []);
    var before = messages.Count;
    Check(guard.ShouldBackOff(), $"Late foreign {kind} must trigger back-off");
    Check(messages.Count == before + 1 && messages[^1].Contains(otherId), "Log must identify the other owner");
    Check(Enumerable.Range(0, 100).All(_ => guard.ShouldBackOff()), "Back-off must persist across repeated calls");
    Check(messages.Count == before + 1, "Do not spam repeated messages");
    Check(!hudGuard.ShouldBackOff(), "Compass patches must not disable the HUD filter");
    registry[compass] = new Patches([ours], [], [], [ours], []);
    Check(!guard.ShouldBackOff(), "Removing the foreign patch must resume filtering");
    Check(messages[^1].Contains("resuming"), "Resumption must be logged");
}
registry[hud] = new Patches([], [theirs], [], [], []);
Check(hudGuard.ShouldBackOff() && !guard.ShouldBackOff(), "HUD patches must not disable the compass filter");
var failed = new QuestFilterBackoff(compass, ownId, messages.Add, _ => throw new InvalidOperationException());
Check(failed.ShouldBackOff(), "Inspection failure must back off safely");
var failureCount = messages.Count;
Check(failed.ShouldBackOff() && messages.Count == failureCount, "Repeated inspection failures must not spam logs");
var plugins = new Dictionary<string, string>();
Check(QuestModCompatibility.BlockingNames(plugins, []).Length == 0, "No installed providers should not block the setting");
plugins[QuestModCompatibility.TrackedHudId] = "Provider metadata name";
Check(QuestModCompatibility.BlockingNames(plugins, []).SequenceEqual(["Tracked Quests HUD"]), "Known installed provider uses its friendly name even before patching");
Check(QuestModCompatibility.BlockingNames(plugins, [QuestModCompatibility.TrackedHudId, QuestModCompatibility.TrackedHudId]).Length == 1, "Known provider and repeated owners must not duplicate the explanation");
plugins.Clear();
plugins[otherId] = "Other Quest Mod";
Check(QuestModCompatibility.BlockingNames(plugins, [otherId]).SequenceEqual(["Other Quest Mod"]), "Unknown catalog entry can resolve through plugin metadata");
Check(QuestModCompatibility.BlockingNames(plugins, ["unidentified.owner"]).Length == 0, "Unidentified Harmony IDs must not leak into the user-facing explanation");
var dependencies = typeof(Plugin).GetCustomAttributes<BepInEx.BepInDependency>().ToArray();
Check(QuestModCompatibility.Providers.All(p => dependencies.Any(d => d.DependencyGUID == p.Id && d.Flags == BepInEx.BepInDependency.DependencyFlags.SoftDependency)), "Every provider must have an optional load-order dependency");
Console.WriteLine($"Passed {checks} quest back-off and compatibility metadata checks.");

static class Targets
{
    public static void Compass() { }
    public static void Hud() { }
    public static void Noop() { }
}
