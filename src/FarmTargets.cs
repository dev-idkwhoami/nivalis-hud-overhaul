using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NivalisMods.HudOverhaul;

// Per-save-slot sidecars, validated against the save's contents. Never append
// fields to the game's positional save format: vanilla must still read it.
internal sealed class FarmTargets
{
    internal const string Auto = "$auto";
    private const string Empty = "$empty";
    private readonly string _directory;
    private string? _slot, _hash;
    private Dictionary<string, string> _targets = new();
    internal FarmTargets(string directory) => _directory = directory;
    // Missing means unconfigured: follow the crop. Explicitly cleared screens
    // need a separate stored value so they remain blank after reloading.
    internal string? Get(string key) => !_targets.TryGetValue(key, out var value)
        ? Auto : value == Empty ? null : value;
    internal void Clear() { _targets = new(); _slot = _hash = null; }
    internal void Load(string slot, string hash)
    {
        Clear();
        var path = FilePath(slot);
        if (File.Exists(path))
        {
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid farm screen targets.");
            if (data.Version != 1 || data.Targets == null) throw new InvalidDataException("Unsupported farm screen target data.");
            if (data.SaveHash == hash) _targets = data.Targets;
        }
        _slot = slot; _hash = hash;
    }
    internal void Set(string key, string? produce)
    {
        var previous = _targets.GetValueOrDefault(key);
        _targets[key] = produce ?? Empty;
        try { if (_slot != null && _hash != null) Save(_slot, _hash); }
        catch { if (previous == null) _targets.Remove(key); else _targets[key] = previous; throw; }
    }
    internal void Save(string slot, string hash)
    {
        Directory.CreateDirectory(_directory);
        var path = FilePath(slot);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Data { SaveHash = hash, Targets = _targets },
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
        _slot = slot; _hash = hash;
    }
    private string FilePath(string slot) => Path.Combine(_directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slot))) + ".json");
    private sealed class Data
    {
        public int Version { get; set; } = 1;
        public string SaveHash { get; set; } = "";
        public Dictionary<string, string> Targets { get; set; } = new();
    }
}
