using HarmonyLib;
using Nivalis;
using Nivalis.Navigation;
using Nivalis.UI;
using UnityEngine;
using UnityEngine.UI;
using MarkerList = Il2CppSystem.Collections.Generic.List<Nivalis.UI.CompassMarkerData>;

namespace NivalisMods.HudOverhaul;

internal static class QuestTracking
{
    private static readonly List<QuestPinState> States = new();
    private static QuestPinFilter _snapshot = new(Array.Empty<QuestPinState>());
    private static int _frame = -1;
    private static IntPtr _manager;
    private static bool _reportedError;

    internal static void Invalidate() => _frame = -1;

    internal static QuestPinFilter Capture(bool fresh = false)
    {
        var manager = QuestManager.Instance;
        var pointer = manager == null ? IntPtr.Zero : manager.Pointer;
        if (!fresh && _frame == Time.frameCount && pointer == _manager) return _snapshot;
        States.Clear();
        if (ModOptions.Quests.Value && manager != null && manager._activeQuests != null)
            foreach (var entry in manager._activeQuests.Values)
                if (entry != null && entry.Quest != null)
                    States.Add(new QuestPinState(entry.Quest.Pointer.ToInt64(), entry.IsActive, entry.Pinned));
        _snapshot = new QuestPinFilter(States);
        _manager = pointer;
        _frame = Time.frameCount;
        return _snapshot;
    }

    internal static long Id(Quest? quest) => quest == null ? 0 : quest.Pointer.ToInt64();

    internal static void Report(Exception error)
    {
        if (_reportedError) return;
        _reportedError = true;
        Plugin.Logger.LogError($"Quest display filtering failed; native displays retained where possible: {error}");
    }
}

// The setter raises native events synchronously, so invalidate before its listeners
// refresh the HUD. Pin preferences and their save/load behavior remain native.
[HarmonyPatch(typeof(RuntimeQuest), nameof(RuntimeQuest.Pinned), MethodType.Setter)]
internal static class QuestPinChangedPatch
{
    [HarmonyPrefix]
    private static void Prefix() => QuestTracking.Invalidate();
}

[HarmonyPatch(typeof(ActiveJournalEntriesUi), nameof(ActiveJournalEntriesUi.Refresh))]
internal static class PinnedHudQuestsPatch
{
    [HarmonyPostfix]
    private static void Postfix(ActiveJournalEntriesUi __instance)
    {
        try
        {
            var filter = QuestTracking.Capture(fresh: true);
            if (!filter.HasPins) return;
            // Native Refresh already puts pinned quests first. Hide its unpinned
            // filler rows only here, never in the journal's shared detail widgets.
            var list = __instance.questList;
            for (var i = 0; i < list.DisplayedCount; i++)
            {
                var go = list.GetItem(i).GameObject;
                var quest = go.GetComponent<ActiveJournalEntryItem>()?._quest?.Quest;
                if (!filter.ShowQuest(QuestTracking.Id(quest))) go.SetActive(false);
            }
            // Venue setup reminders have no journal pin and are native filler too.
            var venues = __instance.venueQuestList;
            if (venues != null)
                for (var i = 0; i < venues.DisplayedCount; i++) venues.GetItem(i).GameObject.SetActive(false);
            // Do not alter either list's pool or DisplayedCount. Native AddItem
            // reactivates reused rows next refresh, including after the last unpin.
            LayoutRebuilder.MarkLayoutForRebuild(__instance.entryParent);
        }
        catch (Exception e) { QuestTracking.Report(e); }
    }
}

[HarmonyPatch(typeof(NavigationUI), nameof(NavigationUI.LateUpdate))]
internal static class PinnedCompassQuestsPatch
{
    private static readonly Stack<MarkerList> Available = new();

    internal sealed class DisplayScope
    {
        internal readonly NavigationManager Manager;
        internal readonly MarkerList Original;
        internal readonly MarkerList Filtered;

        internal DisplayScope(NavigationManager manager, MarkerList original, MarkerList filtered)
        {
            Manager = manager;
            Original = original;
            Filtered = filtered;
        }
    }

    [HarmonyPrefix]
    private static void Prefix(out DisplayScope? __state)
    {
        __state = null;
        MarkerList? filtered = null;
        try
        {
            var filter = QuestTracking.Capture();
            if (!filter.HasPins) return;
            var manager = NavigationManager.Instance;
            if (manager == null || manager._markers == null) return;
            var original = manager._markers;
            filtered = Available.Count > 0 ? Available.Pop() : new MarkerList();
            filtered.Clear();
            for (var i = 0; i < original.Count; i++)
            {
                var marker = original[i];
                if (marker != null && filter.ShowCompassMarker(QuestTracking.Id(marker.Quest))) filtered.Add(marker);
            }
            // Filter BEFORE native grouping, otherwise an unpinned number can
            // remain inside a combined destination icon containing pinned quests.
            // The registered list is never edited. Give only this synchronous draw
            // a filtered view, and restore the original even if native drawing fails.
            __state = new DisplayScope(manager, original, filtered);
            manager._markers = filtered;
        }
        catch (Exception e)
        {
            if (__state != null) __state.Manager._markers = __state.Original;
            __state = null;
            if (filtered != null) { filtered.Clear(); Available.Push(filtered); }
            QuestTracking.Report(e);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(DisplayScope? __state)
    {
        if (__state == null) return;
        __state.Manager._markers = __state.Original;
        __state.Filtered.Clear();
        Available.Push(__state.Filtered);
    }
}
