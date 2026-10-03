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
    private static QuestPinFilter _snapshot = new(Array.Empty<QuestPinState>());
    private static QuestPinFilter _next = new(Array.Empty<QuestPinState>());
    private static QuestManager? _manager;
    private static bool _dirty = true;
    private static bool _reportedError;
    internal static int Revision { get; private set; }
    private static readonly Il2CppSystem.Action Changed = (Il2CppSystem.Action)Invalidate;
    private static readonly Il2CppSystem.Action<Quest> QuestChanged = (Il2CppSystem.Action<Quest>)(_ => Invalidate());

    internal static void Invalidate() => _dirty = true;

    internal static void Bind(QuestManager? manager)
    {
        if (manager?.Pointer == _manager?.Pointer) return;
        Reset();
        _manager = manager;
        if (manager == null) return;
        manager.OnQuestsPinnedChanged.AddListener(Changed);
        manager.OnQuestsUpdated.AddListener(Changed);
        manager.OnQuestStarted.AddListener(QuestChanged);
        manager.OnQuestCompleted.AddListener(QuestChanged);
    }

    internal static void Reset()
    {
        if (_manager != null)
        {
            _manager.OnQuestsPinnedChanged.RemoveListener(Changed);
            _manager.OnQuestsUpdated.RemoveListener(Changed);
            _manager.OnQuestStarted.RemoveListener(QuestChanged);
            _manager.OnQuestCompleted.RemoveListener(QuestChanged);
        }
        _manager = null;
        _snapshot.Clear(); _next.Clear();
        _dirty = true;
        Revision++;
        CompassQuestView.Reset();
    }

    internal static QuestPinFilter Capture(bool fresh = false)
    {
        // The pointer check also handles a manager that predates registration.
        // No quest collection is read until a notification or HUD refresh.
        Bind(QuestManager._instance);
        if (!fresh && !_dirty) return _snapshot;
        _next.Clear();
        if (ModOptions.Quests.Value && _manager != null && _manager._activeQuests != null)
            foreach (var entry in _manager._activeQuests.Values)
                if (entry != null && entry.Quest != null)
                    _next.Add(new QuestPinState(entry.Quest.Pointer.ToInt64(), entry.IsActive, entry.Pinned));
        if (!_snapshot.SameSelection(_next))
        {
            (_snapshot, _next) = (_next, _snapshot);
            Revision++;
        }
        _dirty = false;
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

// These hooks belong to the deferred quest module too: a conflicting provider
// gets no subscriptions, invalidation callbacks, or compass hooks from us.
[HarmonyPatch(typeof(QuestManager))]
internal static class QuestManagerLifecyclePatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(QuestManager.InitializeExternal))]
    private static void Initialized(QuestManager __instance) =>
        Plugin.Guard("Subscribe to quest changes", () => QuestTracking.Bind(__instance));

    [HarmonyPrefix, HarmonyPatch(nameof(QuestManager.OnDestroyInternal))]
    private static void Destroying() => Plugin.Guard("Unsubscribe from quest changes", QuestTracking.Reset);
}

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Clear))]
internal static class QuestSaveLifecyclePatch
{
    [HarmonyPrefix]
    private static void Prefix() => Plugin.Guard("Reset quest selection", QuestTracking.Reset);
}

[HarmonyPatch(typeof(ActiveJournalEntriesUi), nameof(ActiveJournalEntriesUi.Refresh))]
internal static class PinnedHudQuestsPatch
{
    [HarmonyPostfix]
    private static void Postfix(ActiveJournalEntriesUi __instance)
    {
        try
        {
            if (!ModOptions.Quests.Value) return;
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
    internal readonly struct DisplayScope
    {
        internal readonly NavigationManager? Manager;
        internal readonly MarkerList? Original;

        internal DisplayScope(NavigationManager manager, MarkerList original)
        {
            Manager = manager;
            Original = original;
        }
    }

    [HarmonyPrefix]
    private static void Prefix(out DisplayScope __state)
    {
        __state = default;
        try
        {
            // Leave the manager null so our finalizer does nothing when yielding.
            if (!ModOptions.Quests.Value || CompassQuestView.DrawingManager != null) return;
            var filter = QuestTracking.Capture();
            if (!filter.HasPins) return;
            var manager = NavigationManager.Instance;
            if (manager == null || manager._markers == null) return;
            var original = manager._markers;
            var filtered = CompassQuestView.Get(manager, original, filter, QuestTracking.Revision);
            // Native LateUpdate reads _markers directly (the getter is inlined).
            // Select the cached view for this draw; never edit the registry.
            __state = new DisplayScope(manager, original);
            CompassQuestView.DrawingManager = manager;
            CompassQuestView.DrawingSource = original;
            manager._markers = filtered;
        }
        catch (Exception e)
        {
            if (__state.Manager != null && __state.Original != null) __state.Manager._markers = __state.Original;
            __state = default;
            CompassQuestView.EndDraw();
            QuestTracking.Report(e);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(DisplayScope __state)
    {
        if (__state.Manager == null || __state.Original == null) return;
        try { __state.Manager._markers = __state.Original; }
        finally { CompassQuestView.EndDraw(); }
    }
}
