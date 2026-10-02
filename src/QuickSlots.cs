using System.Text.Json;

namespace NivalisMods.HudOverhaul;

// Local preferences, not inventory instances or modifications to game saves.
internal sealed class QuickSlots
{
    internal const int Count = 8;
    internal const string FileName = "HUDOverhaul.quick-slots.json";
    private string?[] _items = new string?[Count];
    private readonly string _path;
    private readonly Action<string> _warn;
    private bool _canWrite = true;

    internal QuickSlots(string path, Action<string> warn)
    {
        _path = path;
        _warn = warn;
        try
        {
            if (!File.Exists(path)) return;
            var items = JsonSerializer.Deserialize<string?[]>(File.ReadAllText(path));
            if (items == null || items.Length != Count || items.Any(x => x != null && string.IsNullOrWhiteSpace(x)))
                throw new JsonException("Expected eight item IDs or null for empty slots.");
            _items = items;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _canWrite = false;
            warn($"Could not load quick slots: {e.Message}. Assignments will last this session; file left unchanged.");
        }
    }

    internal string? this[int slot] => _items[slot];

    internal bool Assign(int slot, string item)
    {
        if (slot < 0 || slot >= Count || string.IsNullOrWhiteSpace(item) || _items[slot] != null) return false;
        _items[slot] = item;
        Save();
        return true;
    }

    internal bool Unassign(int slot)
    {
        if (slot < 0 || slot >= Count || _items[slot] == null) return false;
        _items[slot] = null;
        Save();
        return true;
    }

    private void Save()
    {
        if (_canWrite)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, _path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _warn($"Could not save quick slots: {e.Message}. Change remains active this session.");
            }
        }
    }
}
