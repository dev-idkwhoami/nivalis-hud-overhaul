namespace NivalisMods.HudOverhaul;

internal static class ReviewModes
{
    internal static readonly SortMode[] All =
    [
        new(Labels.Get("sort.original"), SortKey.Native),
        new(Labels.Get("reviews.time.desc"), SortKey.ReviewTime, true),
        new(Labels.Get("reviews.time.asc"), SortKey.ReviewTime),
        new(Labels.Get("reviews.stars.desc"), SortKey.Stars, true),
        new(Labels.Get("reviews.stars.asc"), SortKey.Stars),
        new(Labels.Get("reviews.positive"), SortKey.Sentiment, true),
        new(Labels.Get("reviews.negative"), SortKey.Sentiment)
    ];
}
