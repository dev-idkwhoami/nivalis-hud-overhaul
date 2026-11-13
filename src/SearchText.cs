namespace NivalisMods.HudOverhaul;

internal static class SearchText
{
    internal static string[] Terms(string? query) =>
        query?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];

    // Match every word, allowing different words to match different metadata
    // fields, as in the game's Update #9 search.
    internal static bool Matches(string? query, IEnumerable<string?> fields)
    {
        var values = fields.ToArray();
        return Terms(query).All(term => values.Any(field =>
            !string.IsNullOrEmpty(field) && field.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
