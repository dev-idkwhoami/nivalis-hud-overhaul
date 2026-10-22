namespace NivalisMods.HudOverhaul;

internal enum SortKey { Native, Calories, Exclusivity, Comfort, ReviewTime, Stars, Sentiment, Updated, Active }
internal sealed record SortMode(string Label, SortKey Key, bool Descending = false);

internal static class SortOrder
{
    internal static int RemapSelection(IReadOnlyList<int> order, int selected)
    {
        if (selected < 0) return -1;
        for (var i = 0; i < order.Count; i++) if (order[i] == selected) return i;
        return -1;
    }

    // Missing values always go last. Ties retain the game's existing order.
    internal static int[] Indices(IReadOnlyList<double?> values, bool descending)
    {
        return Enumerable.Range(0, values.Count)
            .OrderBy(i => !values[i].HasValue || double.IsNaN(values[i]!.Value))
            .ThenBy(i => values[i].HasValue && !double.IsNaN(values[i]!.Value)
                ? (descending ? -values[i]!.Value : values[i]!.Value) : 0)
            .ThenBy(i => i)
            .ToArray();
    }
}
