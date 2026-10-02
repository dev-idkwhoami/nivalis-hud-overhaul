namespace NivalisMods.HudOverhaul;

internal sealed record HistoryEvent
{
    public string Kind { get; init; } = "";
    public string Venue { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Name { get; init; } = "";
    public int Seconds { get; init; }
    public int Day { get; init; }
    public long Quantity { get; init; }
    public long Money { get; init; }
    public long? Before { get; init; }
    public long? After { get; init; }
    public string Group { get; init; } = "";
    public string Source { get; init; } = "live";
}

// Raw steps retain exact game timestamps. Only presentation summaries coalesce.
internal sealed class HistoryAdjustments
{
    private sealed record Pending(HistoryEvent First, HistoryEvent Last, double At, int Steps);
    private readonly Dictionary<string, Pending> _pending = new();
    internal IEnumerable<HistoryEvent> Change(HistoryEvent step, double now)
    {
        var key = step.Kind + ":" + step.Venue + ":" + step.Subject;
        if (_pending.TryGetValue(key, out var old) && (now - old.At >= 2 || old.Last.After != step.Before))
        { _pending.Remove(key); yield return Summary(old); old = null; }
        var group = old?.First.Group ?? Guid.NewGuid().ToString("N");
        step = step with { Group = group };
        _pending[key] = new Pending(old?.First ?? step, step, now, (old?.Steps ?? 0) + 1);
        yield return step;
    }
    internal IEnumerable<HistoryEvent> Flush(double now, bool all = false)
    {
        foreach (var pair in _pending.ToArray())
            if (all || now - pair.Value.At >= 2)
            { _pending.Remove(pair.Key); yield return Summary(pair.Value); }
    }
    internal void Clear() => _pending.Clear();
    private static HistoryEvent Summary(Pending p) => p.Last with
    {
        Kind = p.First.Kind.Replace("_step", "_adjustment"),
        Before = p.First.Before,
        Quantity = p.Steps,
        Source = "grouped_steps"
    };
}
