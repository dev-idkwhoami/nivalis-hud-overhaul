namespace NivalisMods.HudOverhaul;

internal static class SearchText
{
    // Keep the game's whole-query substring behavior; each searchable field is
    // separate so a query cannot accidentally span two unrelated tag names.
    internal static bool Matches(string? query, IEnumerable<string?> fields) =>
        string.IsNullOrWhiteSpace(query) || fields.Any(field =>
            !string.IsNullOrEmpty(field) && field.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
}
