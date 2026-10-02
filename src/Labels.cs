using System.Reflection;
using System.Text.Json;

namespace NivalisMods.HudOverhaul;

internal static class Labels
{
    private static readonly Dictionary<string, string> Defaults = ReadDefaults();
    private static Dictionary<string, string> _values = new(Defaults);
    internal const string FileName = "HUDOverhaul.labels.json";

    private static Dictionary<string, string> ReadDefaults()
    {
        using var stream = typeof(Labels).Assembly.GetManifestResourceStream("HudOverhaul.Labels.en.json")
            ?? throw new InvalidOperationException("Embedded HUD labels missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    internal static string Get(string key) => _values.TryGetValue(key, out var value) ? value : key;

    internal static string ReviewDate(string time, string day) => Get("reviews.timestamp")
        .Replace("{time}", time).Replace("{day}", day);

    internal static void Load(string path, Action<string> warn)
    {
        _values = new(Defaults);
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Never overwrite a user's translations, including during updates.
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                using var defaults = typeof(Labels).Assembly.GetManifestResourceStream("HudOverhaul.Labels.en.json")!;
                defaults.CopyTo(output);
                return;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected an object mapping label keys to strings.");
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!Defaults.ContainsKey(property.Name))
                {
                    warn($"Unknown label key '{property.Name}' in {path}; ignored.");
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    warn($"Invalid label '{property.Name}'; using English default.");
                    continue;
                }
                var value = property.Value.GetString()!;
                if (property.Name == "reviews.timestamp" && (!value.Contains("{time}") || !value.Contains("{day}")))
                {
                    warn("reviews.timestamp must contain {time} and {day}; using English default.");
                    continue;
                }
                _values[property.Name] = value;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            warn($"Could not load HUD labels from {path}: {e.Message}. English defaults remain available; file left unchanged.");
        }
    }
}
