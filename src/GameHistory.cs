using HarmonyLib;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Player;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class GameHistory
{
    private static HistoryWorker? _worker;
    private static volatile bool _storageFailed;
    private static readonly List<HistoryEvent> Buffer = new();
    private static readonly HashSet<string> Errors = new();
    private static string? _pendingSave;
    private static float _nextFlush;
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
        // Do not carry buffered payments across save boundaries.
        Active = false; Buffer.Clear();
    }
    private static void Start()
    {
        var name = _pendingSave; _pendingSave = null;
        Clear();
        if (PlayerManager._instance?.LocalPlayer == null) return;
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
        // Preserve the existing one-time payroll receipt import for new branches.
        if (!known)
        {
            try { ImportStaffReceipts(); }
            catch { Clear(); throw; }
        }
        Flush();
        // Map an existing save immediately, so repeated loads don't reimport it.
        if (name != null) _worker.Post(db => db.Checkpoint(hash, name));
        Plugin.Verbose($"Payroll history ready: {(known ? "resumed saved branch" : "new branch; available staff receipts imported")}. File history writes run on a worker.");
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
    internal static void Tick()
    {
        if (_storageFailed) { Clear(); return; }
        if (!Active) return;
        // The payroll writer outlives gameplay; do not create managers at the title screen.
        var playerManager = PlayerManager._instance;
        if (playerManager == null || playerManager.LocalPlayer == null) { Clear(); return; }
        var scenes = GameSceneManager._instance;
        if (GameSceneManager.IsUnloadingGameplay || (scenes != null && scenes.IsLoading)) return;
        if (Time.unscaledTime >= _nextFlush || Buffer.Count >= 256) Flush();
    }
    internal static void Flush()
    {
        if (!Active || _worker == null) return;
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
        Flush();
        var hash = Payroll.SaveHash(name);
        _worker.Post(db => db.Checkpoint(hash, name));
    }
    internal static void Shutdown()
    {
        Flush(); _worker?.Dispose(); _worker = null; Active = false;
    }
    private static void ImportStaffReceipts()
    {
        var owned = new Il2CppSystem.Collections.Generic.List<Venue>();
        PlayerManager._instance.LocalPlayer.GetOwnedVenues(owned);
        for (var i = 0; i < owned.Count; i++)
        {
            var venue = owned[i];
            if (venue != null && venue.PlayerOwned && venue.RuntimeData != null) ImportStaffReceipts(venue);
        }
    }
    private static void ImportStaffReceipts(Venue venue)
    {
        var lookup = venue.RuntimeData.Receipts.ReceiptsLookup;
        var wages = lookup.GetReceiptsOfType<StaffReceipt>();
        var n = wages.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<StaffReceipt>>().Count;
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
