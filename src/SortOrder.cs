namespace NivalisMods.HudOverhaul;

internal enum SortKey { Native, Calories, Exclusivity, Comfort, ReviewTime, Stars, Sentiment, Updated, Active }
internal sealed record SortMode(string Label, SortKey Key, bool Descending = false);

internal static class SortOrder
{
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
