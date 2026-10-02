using System.Text.Json;

namespace NivalisMods.HudOverhaul;

// Separate from native receipts: those merge all employees and lose identity.
internal sealed class PayrollLedger
{
    public sealed class Payment
    {
        public string Venue { get; set; } = "";
        public string Person { get; set; } = "";
        public string Name { get; set; } = "";
        public int Day { get; set; }
        public int Count { get; set; }
        public long Amount { get; set; }
    }
    public sealed class Snapshot
    {
        public string SaveHash { get; set; } = "";
        public List<Payment> Payments { get; set; } = new();
    }
    internal List<Payment> Payments = new();
    internal void Add(string venue, string person, string name, int day, int count, long amount)
    {
        if (count <= 0 || amount > 0) return;
        var entry = Payments.Find(p => p.Venue == venue && p.Person == person && p.Day == day);
        if (entry == null) Payments.Add(entry = new Payment { Venue = venue, Person = person, Day = day });
        entry.Name = name;
        entry.Count += count;
        entry.Amount += amount;
    }
    internal void Save(string path, string hash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Snapshot { SaveHash = hash, Payments = Payments }));
        File.Move(path + ".tmp", path, true);
    }
    internal void Load(string path, string hash)
    {
        Payments.Clear();
        if (!File.Exists(path)) return;
        var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
        if (saved?.SaveHash != hash || saved.Payments == null) return;
        if (saved.Payments.Any(p => p == null || string.IsNullOrEmpty(p.Person) ||
            string.IsNullOrEmpty(p.Venue) || p.Count <= 0 || p.Amount > 0))
            throw new JsonException("Invalid payroll entries.");
        Payments = saved.Payments;
    }
}
