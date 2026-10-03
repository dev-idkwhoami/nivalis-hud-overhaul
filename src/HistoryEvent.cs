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
