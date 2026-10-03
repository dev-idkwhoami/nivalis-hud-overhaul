namespace NivalisMods.HudOverhaul;

internal readonly record struct QuestPinState(long QuestId, bool Active, bool Pinned);

// A display-only snapshot: completed/failed quests cannot keep filtering enabled.
internal sealed class QuestPinFilter
{
    private readonly HashSet<long> _pinned = new();

    internal QuestPinFilter(IEnumerable<QuestPinState> quests)
    {
        foreach (var quest in quests) Add(quest);
    }

    internal void Clear() => _pinned.Clear();
    internal void Add(QuestPinState quest)
    {
        if (quest.Active && quest.Pinned && quest.QuestId != 0) _pinned.Add(quest.QuestId);
    }

    internal bool HasPins => _pinned.Count != 0;
    internal bool SameSelection(QuestPinFilter other) => _pinned.SetEquals(other._pinned);
    internal bool ShowQuest(long questId) => !HasPins || _pinned.Contains(questId);
    // Ordinary compass destinations have no owning quest and remain visible.
    internal bool ShowCompassMarker(long questId) => questId == 0 || ShowQuest(questId);
}
