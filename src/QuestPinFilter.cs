namespace NivalisMods.HudOverhaul;

internal readonly record struct QuestPinState(long QuestId, bool Active, bool Pinned);

// A display-only snapshot: completed/failed quests cannot keep filtering enabled.
internal sealed class QuestPinFilter
{
    private readonly HashSet<long> _pinned;

    internal QuestPinFilter(IEnumerable<QuestPinState> quests) => _pinned = quests
        .Where(quest => quest.Active && quest.Pinned && quest.QuestId != 0)
        .Select(quest => quest.QuestId).ToHashSet();

    internal bool HasPins => _pinned.Count != 0;
    internal bool ShowQuest(long questId) => !HasPins || _pinned.Contains(questId);
    // Ordinary compass destinations have no owning quest and remain visible.
    internal bool ShowCompassMarker(long questId) => questId == 0 || ShowQuest(questId);
}
