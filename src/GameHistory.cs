using BepInEx;
using HarmonyLib;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Player;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class GameHistory
{
    private static HistoryWorker? _worker;
    private static volatile bool _storageFailed;
    private static readonly List<HistoryEvent> Buffer = new();
    private static readonly HistoryAdjustments Adjustments = new();
    private static readonly Dictionary<string, long> Values = new();
    private static readonly Dictionary<IntPtr, Venue> Inventories = new();
    private static readonly HashSet<string> KnownVenues = new();
    private static readonly HashSet<string> Errors = new();
    private static string? _pendingSave;
    private static float _nextScan, _nextFlush;
    private static int _stockDay;
    internal static bool Active { get; private set; }
    internal static void Initialize()
    {
        SerializationManager.OnPostLoad.Add((Il2CppSystem.Action)(() => Safe("load", Start)));
    }
    internal static void Safe(string operation, Action action)
    {
        try { action(); }
        catch (Exception e) { if (Errors.Add(operation)) Plugin.Logger.LogError($"History {operation}: {e}"); }
    }
    internal static void BeginLoad(string name) { Clear(); _pendingSave = name; }
    internal static void Clear()
    {
        // Do not flush pending summaries across save boundaries.
        Active = false; Buffer.Clear(); Adjustments.Clear(); Values.Clear();
        Inventories.Clear(); KnownVenues.Clear(); SaleCapture.Current = null;
    }
    private static void Start()
    {
        var name = _pendingSave; _pendingSave = null;
        Clear();
        _worker ??= new HistoryWorker(Path.Combine(ModStorage.Root, "HUDOverhaul.history.log"),
            e => { _storageFailed = true; Plugin.Logger.LogError($"History storage stopped; gameplay is unaffected: {e}"); });
        var hash = name == null ? "" : Payroll.SaveHash(name);
        var known = _worker.Run(db => db.Fork(hash));
        var legacy = Payroll.Ledger.Payments.ToArray();
        if (known)
        {
            var records = _worker.Run(db => db.Read("staff_payment"));
            Payroll.Ledger.Payments.Clear();
            foreach (var e in records)
                if (e.Subject.Length != 0) Payroll.Ledger.Add(e.Venue, e.Subject, e.Name, e.Day, checked((int)e.Quantity), e.Money);
        }
        Active = true;
        if (!known)
            foreach (var p in legacy) Record(new HistoryEvent
            {
                Kind = "staff_payment",
                Venue = p.Venue,
                Subject = p.Person,
                Name = p.Name,
                Day = p.Day,
                Seconds = 0,
                Quantity = p.Count,
                Money = p.Amount,
                Source = "legacy_daily_import"
            });
        try { Scan(importReceipts: !known, snapshot: true); }
        catch { Clear(); throw; }
        if (!Active) return;
        _stockDay = TimeOfDayManager.CurrentTime.GameplayGameDay;
        _nextScan = Time.unscaledTime + 5;
        Flush();
        // Map an existing save immediately, so repeated loads don't reimport it.
        if (name != null) _worker.Post(db => db.Checkpoint(hash, name));
        Plugin.Verbose($"History recording ready: {(known ? "resumed saved branch" : "new branch; available receipts imported")}. File history writes run on a worker.");
    }
    internal static HistoryEvent Event(string kind, string venue, string subject, string name) => new()
    {
        Kind = kind,
        Venue = venue,
        Subject = subject,
        Name = name,
        Seconds = TimeOfDayManager.CurrentTime.TotalGameSeconds,
        Day = TimeOfDayManager.CurrentTime.GameplayGameDay
    };
    internal static void Record(HistoryEvent e)
    {
        if (!Active) return;
        Buffer.Add(e);
    }
    internal static void Change(string kind, string venue, string subject, string name, long before, long after, string source)
    {
        if (!Active || venue.Length == 0) return;
        var key = kind + ":" + venue + ":" + subject;
        if (!Values.ContainsKey(key)) Record(Event(kind + "_baseline", venue, subject, name) with { After = before, Source = "first_observed" });
        Values[key] = after;
        if (before == after) return;
        foreach (var e in Adjustments.Change(Event(kind + "_step", venue, subject, name) with { Before = before, After = after, Source = source }, Time.unscaledTime)) Record(e);
    }
    private static void Observe(string kind, string venue, string subject, string name, long value)
    {
        var key = kind + ":" + venue + ":" + subject;
        if (Values.TryGetValue(key, out var old))
        { if (old != value) Change(kind, venue, subject, name, old, value, "observed_outside_buttons"); }
        else { Values[key] = value; Record(Event(kind + "_baseline", venue, subject, name) with { After = value, Source = "baseline" }); }
    }
    internal static void Tick()
    {
        if (_storageFailed) { Clear(); return; }
        if (!Active) return;
        // The persistent pump outlives gameplay. Never let a title-screen scan
        // create a replacement PlayerManager through the lazy Instance getter.
        var playerManager = PlayerManager._instance;
        if (playerManager == null || playerManager.LocalPlayer == null) { Clear(); return; }
        var scenes = GameSceneManager._instance;
        if (GameSceneManager.IsUnloadingGameplay || (scenes != null && scenes.IsLoading)) return;
        foreach (var e in Adjustments.Flush(Time.unscaledTime)) Record(e);
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 5;
            var day = TimeOfDayManager.CurrentTime.GameplayGameDay;
            Scan(false, day != _stockDay); _stockDay = day;
        }
        if (Time.unscaledTime >= _nextFlush || Buffer.Count >= 256) Flush();
    }
    internal static void Flush(bool all = false)
    {
        if (!Active || _worker == null) return;
        if (all) foreach (var e in Adjustments.Flush(Time.unscaledTime, true)) Record(e);
        if (Buffer.Count != 0)
        {
            var events = Buffer.ToArray();
            _worker.Post(db => db.Append(events));
            Buffer.Clear();
        }
        _nextFlush = Time.unscaledTime + 2;
    }
    internal static void Save(string name)
    {
        if (!Active || _worker == null) return;
        Flush(true);
        var hash = Payroll.SaveHash(name);
        _worker.Post(db => db.Checkpoint(hash, name));
    }
    internal static void Shutdown()
    {
        Flush(true); _worker?.Dispose(); _worker = null; Active = false;
    }
    internal static bool Owned(Venue? venue) => venue != null && venue.PlayerOwned;
    internal static void StockChange(VenueInventory inventory, ItemTypeCountChange change)
    {
        if (!Active || change.Type == null || change.CountChange == 0 || !Inventories.TryGetValue(inventory.Pointer, out var venue)) return;
        Record(Event("stock_movement", venue.Guid, change.Type.Guid, change.Type.Name) with
        { Quantity = change.CountChange, Source = "venue_inventory_notification" });
    }
    private static void Scan(bool importReceipts, bool snapshot)
    {
        var manager = PlayerManager._instance;
        if (manager == null || manager.LocalPlayer == null) { Clear(); return; }
        var owned = new Il2CppSystem.Collections.Generic.List<Venue>();
        manager.LocalPlayer.GetOwnedVenues(owned);
        for (var v = 0; v < owned.Count; v++)
        {
            var venue = owned[v]; var runtime = venue.RuntimeData;
            if (runtime == null) continue;
            var first = KnownVenues.Add(venue.Guid);
            if (first) Record(Event("venue_baseline", venue.Guid, venue.Guid, venue.EntryName));
            var inventory = runtime.InventoryData.itemContainer;
            Inventories[inventory.Pointer] = venue;
            if (runtime.JointInventory != null) Inventories[runtime.JointInventory.Pointer] = venue;
            var menu = runtime.Menu;
            for (var m = 0; m < menu.Count; m++)
            {
                var meal = menu[m]; var item = meal.Meal;
                if (item != null) Observe("meal_price", venue.Guid, item.Guid, item.Name, meal.Price);
            }
            var staff = runtime.Staff;
            var n = staff.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Nivalis.GhostSystem.Ai.Person>>().Count;
            for (var s = 0; s < n; s++)
            {
                var person = staff[s];
                Observe("staff_wage", venue.Guid, person.Guid, person.Name, person.RuntimeData.Wage);
            }
            if (snapshot || first)
            {
                // Each snapshot is complete, including an empty inventory marker.
                var group = Guid.NewGuid().ToString("N");
                Record(Event("stock_snapshot_begin", venue.Guid, "", "") with { Group = group, Source = "observed_snapshot" });
                var items = inventory.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ItemStack>>().GetEnumerator();
                var counts = new Dictionary<ItemType, long>();
                while (items.Cast<Il2CppSystem.Collections.IEnumerator>().MoveNext()) { var stack = items.Current; if (stack?.Type != null) counts[stack.Type] = counts.GetValueOrDefault(stack.Type) + stack.StackCount; }
                foreach (var pair in counts)
                    Record(Event("stock_snapshot", venue.Guid, pair.Key.Guid, pair.Key.Name) with { Quantity = pair.Value, Group = group, Source = "observed_snapshot" });
            }
            // Import only at tracking start. Newly acquired venues may already
            // have live events; importing their receipts again would double count.
            if (importReceipts) ImportReceipts(venue);
        }
    }
    private static void ImportReceipts(Venue venue)
    {
        var lookup = venue.RuntimeData.Receipts.ReceiptsLookup;
        var meals = lookup.GetReceiptsOfType<RestaurantReceipt>();
        var n = meals.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<RestaurantReceipt>>().Count;
        for (var i = 0; i < n; i++)
        {
            var r = meals[i]; if (r.Meal == null) continue;
            Record(new HistoryEvent
            {
                Kind = "meal_sale",
                Venue = venue.Guid,
                Subject = r.Meal.Guid,
                Name = r.Meal.Name,
                Seconds = r.Time.TotalGameSeconds,
                Day = r.Time.GameplayGameDay,
                Quantity = r.Count,
                Money = r.Amount,
                Source = "merged_receipt_import"
            });
        }
        var wages = lookup.GetReceiptsOfType<StaffReceipt>();
        n = wages.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<StaffReceipt>>().Count;
        var byDay = new Dictionary<int, (long Count, long Money)>();
        for (var i = 0; i < n; i++)
        { var r = wages[i]; var old = byDay.GetValueOrDefault(r.Time.GameplayGameDay); byDay[r.Time.GameplayGameDay] = (old.Count + r.Count, old.Money + r.Amount); }
        foreach (var pair in byDay)
        {
            var details = Payroll.Ledger.Payments.Where(p => p.Venue == venue.Guid && p.Day == pair.Key).ToArray();
            var count = pair.Value.Count - details.Sum(p => (long)p.Count); var money = pair.Value.Money - details.Sum(p => p.Amount);
            if (count > 0 || money < 0) Record(new HistoryEvent
            {
                Kind = "staff_payment",
                Venue = venue.Guid,
                Day = pair.Key,
                Quantity = Math.Max(0, count),
                Money = Math.Min(0, money),
                Source = "unattributed_receipt_import"
            });
        }
    }
}

public sealed class HistoryPump : MonoBehaviour
{
    public HistoryPump(IntPtr pointer) : base(pointer) { }
    public void Update() => GameHistory.Safe("tick", GameHistory.Tick);
    public void OnApplicationQuit() => GameHistory.Safe("shutdown", GameHistory.Shutdown);
}

[HarmonyPatch(typeof(GameSceneManager), nameof(GameSceneManager.UnloadGameplay))]
internal static class HistoryUnloadPatch
{
    // Stop before the unload coroutine removes managers, not on the next save load.
    [HarmonyPrefix]
    private static void Prefix() => GameHistory.Clear();
}
