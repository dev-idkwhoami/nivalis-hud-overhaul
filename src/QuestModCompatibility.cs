using BepInEx;

namespace NivalisMods.HudOverhaul;

// Add a provider below and its optional load-order dependency here. It remains
// optional: HUD Overhaul works normally when the other mod is not installed.
[BepInDependency(QuestModCompatibility.TrackedHudId, BepInDependency.DependencyFlags.SoftDependency)]
public sealed partial class Plugin { }

internal static class QuestModCompatibility
{
    internal const string TrackedHudId = "hvizeu.nivalis.trackedquestshud";
    internal sealed record Provider(string Id, string Name, params string[] HarmonyIds);

    internal static readonly Provider[] Providers =
    {
        new(TrackedHudId, "Tracked Quests HUD")
    };

    internal static string[] BlockingNames(IReadOnlyDictionary<string, string> plugins, IEnumerable<string> patchOwners)
    {
        var names = Providers.Where(p => plugins.ContainsKey(p.Id)).Select(p => p.Name).ToList();
        foreach (var owner in patchOwners)
        {
            var known = Providers.FirstOrDefault(p => p.Id == owner || p.HarmonyIds.Contains(owner));
            if (known != null) names.Add(known.Name);
            else if (plugins.TryGetValue(owner, out var name)) names.Add(name);
        }
        return names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }
}
