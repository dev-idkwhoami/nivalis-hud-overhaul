using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Nivalis.Locale.UI;
using Nivalis.UI;
using Nivalis.GhostSystem.CustomerLoop;

namespace NivalisMods.HudOverhaul;

// Diagnostic counters only: preserve the game's loading path until a live
// capture establishes which work dominates. Nested samples are inclusive.
internal static class PerformanceTrace
{
    [ThreadStatic] private static Capture? _active;
    internal sealed class Capture
    {
        internal readonly string Name;
        internal readonly long Start = Stopwatch.GetTimestamp();
        internal readonly Capture? Parent;
        internal readonly Dictionary<string, (int Count, long Ticks)> Samples = new();
        internal Capture(string name) { Name = name; Parent = _active; }
    }
    internal static Capture? Begin(string name)
    {
        if (!Plugin.IsVerbose) return null;
        var capture = new Capture(name);
        _active = capture;
        return capture;
    }
    internal static long StartSample() => _active == null ? 0 : Stopwatch.GetTimestamp();
    internal static void Sample(string name, long start)
    {
        if (start == 0 || _active == null) return;
        var previous = _active.Samples.GetValueOrDefault(name);
        _active.Samples[name] = (previous.Count + 1, previous.Ticks + Stopwatch.GetTimestamp() - start);
    }
    internal static void End(Capture? capture)
    {
        if (capture == null) return;
        var elapsed = Stopwatch.GetTimestamp() - capture.Start;
        _active = capture.Parent;
        var samples = string.Join("; ", capture.Samples.OrderByDescending(x => x.Value.Ticks)
            .Select(x => $"{x.Key}: {x.Value.Count} calls / {Ms(x.Value.Ticks):F1} ms"));
        Plugin.Verbose($"PERF {capture.Name}: {Ms(elapsed):F1} ms synchronous; {samples}");
    }
    internal static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}

[HarmonyPatch]
internal static class RefreshTimingPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LocaleReviewOverviewTab), nameof(LocaleReviewOverviewTab.RefreshReviewList));
        yield return AccessTools.Method(typeof(ShopUiVendorPanel), nameof(ShopUiVendorPanel.Refresh));
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(MethodBase __originalMethod, out PerformanceTrace.Capture? __state) =>
        __state = PerformanceTrace.Begin($"{__originalMethod.DeclaringType!.Name}.{__originalMethod.Name}");
    [HarmonyFinalizer]
    private static void Finalizer(PerformanceTrace.Capture? __state) => PerformanceTrace.End(__state);
}

[HarmonyPatch]
internal static class LoadingStageTimingPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        // Do not detour BeginUpdate: its nullable value argument fails native
        // trampoline marshalling when absent, skipping the actual list reset.
        // Time reference-type row Show instead of the value-type Review method.
        foreach (var name in new[] { "EndUpdate", "AddItem", "CreateNewItemDisplay" })
            yield return AccessTools.Method(typeof(ItemListUI), name);
        yield return AccessTools.Method(typeof(ReviewItemDisplayUi), nameof(ReviewItemDisplayUi.Show));
        yield return AccessTools.Method(typeof(ShopUiVendorPanel), "CreateEntry");
        yield return AccessTools.Method(typeof(ShopItemDisplayUI), nameof(ShopItemDisplayUI.Initialize));
        // The concrete overloads can be timed safely; generic shared methods
        // need separate handling in IL2CPP and are deliberately excluded.
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(InventoryItemFilteringUi)))
            if (method.Name == "FilterResult" && !method.ContainsGenericParameters) yield return method;
    }
    [HarmonyPrefix]
    private static void Prefix(out long __state) => __state = PerformanceTrace.StartSample();
    [HarmonyFinalizer]
    private static void Finalizer(MethodBase __originalMethod, long __state)
    {
        if (__state != 0) PerformanceTrace.Sample($"{__originalMethod.DeclaringType!.Name}.{__originalMethod.Name}", __state);
    }
}
