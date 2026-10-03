using System.Reflection;
using HarmonyLib;
using Nivalis.Navigation;
using MarkerList = Il2CppSystem.Collections.Generic.List<Nivalis.UI.CompassMarkerData>;

namespace NivalisMods.HudOverhaul;

internal static class CompassQuestView
{
    private static MarkerList? _view, _source;
    private static IntPtr _manager;
    private static int _sourceVersion, _selectionRevision;
    private static bool _valid;
    internal static NavigationManager? DrawingManager;
    internal static MarkerList? DrawingSource;

    internal static MarkerList Get(NavigationManager manager, MarkerList source, QuestPinFilter filter, int revision)
    {
        // The native list's version changes on add/remove/reorder/replacement.
        // CompassMarker.Quest unregisters and re-registers its marker when the
        // association changes. Moving a target doesn't change list membership.
        if (_valid && _manager == manager.Pointer && _source?.Pointer == source.Pointer &&
            _sourceVersion == source._version && _selectionRevision == revision) return _view!;

        _valid = false;
        _view ??= new MarkerList();
        _view.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            var marker = source[i];
            if (marker != null && filter.ShowCompassMarker(QuestTracking.Id(marker.Quest))) _view.Add(marker);
        }
        _source = source;
        _manager = manager.Pointer;
        _sourceVersion = source._version;
        _selectionRevision = revision;
        _valid = true;
        return _view;
    }

    internal static void Reset()
    {
        _valid = false;
        _source = null;
        // Never clear a list while native drawing is enumerating it.
        if (DrawingManager == null) _view?.Clear();
    }

    internal static void EndDraw() { DrawingManager = null; DrawingSource = null; }
}

// If native drawing triggers an enable/disable callback, registrations still
// belong to the original registry, never the temporary display view. The list
// version then invalidates the cache for the next draw.
[HarmonyPatch]
internal static class CompassRegistryPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NavigationManager), nameof(NavigationManager.RegisterCompassMarker));
        yield return AccessTools.Method(typeof(NavigationManager), nameof(NavigationManager.UnregisterCompassMarker));
    }

    [HarmonyPrefix]
    private static void Prefix(NavigationManager __instance, out MarkerList? __state)
    {
        __state = null;
        var source = CompassQuestView.DrawingSource;
        if (source == null || CompassQuestView.DrawingManager?.Pointer != __instance.Pointer ||
            __instance._markers.Pointer == source.Pointer) return;
        __state = __instance._markers;
        __instance._markers = source;
    }

    [HarmonyFinalizer]
    private static void Finalizer(NavigationManager __instance, MarkerList? __state)
    {
        if (__state != null) __instance._markers = __state;
    }
}
