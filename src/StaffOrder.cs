using System.Text.Json;

namespace NivalisMods.HudOverhaul;

// UI preferences only. Never changes the game's staff collection or save files.
internal sealed class StaffOrder
{
    internal const string FileName = "HUDOverhaul.staff-order.json";
    private Dictionary<string, string[]> _venues = new();
    private readonly string _path;
    private readonly Action<string> _warn;
    private bool _canWrite = true;

    internal StaffOrder(string path, Action<string> warn)
    {
        _path = path;
        _warn = warn;
        try
        {
            if (File.Exists(path))
                _venues = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))
                    ?? throw new JsonException("Expected venue order object.");
            if (_venues.Any(pair => pair.Value == null || pair.Value.Any(string.IsNullOrWhiteSpace)))
                throw new JsonException("Expected arrays of employee IDs.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _venues = new();
            _canWrite = false;
            warn($"Could not load staff order: {e.Message}. Orders will only last this session; file left unchanged.");
        }
    }

    internal string[] Resolve(string venue, IEnumerable<string> current)
    {
        var live = current.Distinct().ToArray();
        var saved = _venues.TryGetValue(venue, out var order) ? order : Array.Empty<string>();
        var present = live.ToHashSet();
        var resolved = saved.Where(present.Contains).Concat(live).Distinct().ToArray();
        // Reconcile hires/firings only for a venue the player has arranged.
        if (_venues.ContainsKey(venue) && !saved.SequenceEqual(resolved)) Remember(venue, resolved);
        return resolved;
    }

    internal void Remember(string venue, string[] order)
    {
        _venues[venue] = order.ToArray();
        if (!_canWrite) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_venues, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _warn($"Could not save staff order: {e.Message}. Current session order is retained.");
        }
    }
}
