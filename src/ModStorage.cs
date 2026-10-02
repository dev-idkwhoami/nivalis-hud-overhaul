using System.Security.Cryptography;

namespace NivalisMods.HudOverhaul;

internal static class ModStorage
{
    internal static string Root { get; private set; } = null!;
    internal static string FilePath(string name) => Path.Combine(Root, name);
    private static readonly string[] LegacyNames =
    {
        "HUDOverhaul.labels.json", "HUDOverhaul.quick-slots.json",
        "HUDOverhaul.staff-order.json", "HUDOverhaul.farm-screen-preview.json", "HUDOverhaul.history.log",
        "HUDOverhaul.farm-targets", "HUDOverhaul.payroll"
    };

    // Preserve gameplay data and translations. Configuration is owned by Mod
    // Companion; old .cfg files are deliberately neither read nor migrated.
    internal static void Initialize(string configRoot)
    {
        Root = Path.Combine(configRoot, "HUDOverhaul");
        Directory.CreateDirectory(Root);
        var moves = new List<(string Source, string Target)>();
        foreach (var name in LegacyNames)
        {
            var source = Path.Combine(configRoot, name);
            var target = FilePath(name);
            if (File.Exists(source)) moves.Add((source, target));
            else if (Directory.Exists(source))
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    moves.Add((file, Path.Combine(target, Path.GetRelativePath(source, file))));
        }
        // Check every conflict before moving anything. Never silently replace
        // a different data file with another copy.
        foreach (var (source, target) in moves)
            if (Directory.Exists(target) || (File.Exists(target) && !Identical(source, target)))
                throw new IOException($"HUD Overhaul migration conflict: '{source}' and '{target}'. Both were preserved; resolve the duplicate before loading the mod.");
        foreach (var (source, target) in moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) File.Delete(source); // Verified identical above.
            else File.Move(source, target);
        }
        foreach (var name in LegacyNames)
        {
            var directory = Path.Combine(configRoot, name);
            if (!Directory.Exists(directory)) continue;
            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                if (!Directory.EnumerateFileSystemEntries(child).Any()) Directory.Delete(child);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    private static bool Identical(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using var hash = SHA256.Create();
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return hash.ComputeHash(a).SequenceEqual(hash.ComputeHash(b));
    }
}
