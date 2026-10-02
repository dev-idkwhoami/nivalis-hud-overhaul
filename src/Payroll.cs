using System.Security.Cryptography;
using System.Text;
using BepInEx;
using HarmonyLib;
using Nivalis;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using Nivalis.Player;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class Payroll
{
    internal static readonly PayrollLedger Ledger = new();
    internal static void Initialize() => SerializationManager.OnPostLoad.Add((Il2CppSystem.Action)EnsureLoaded);
    private static string? _pendingSave;
    internal static void BeginLoad(string name) { Ledger.Payments.Clear(); _pendingSave = name; GameHistory.BeginLoad(name); }
    internal static void EnsureLoaded()
    {
        if (_pendingSave == null) return;
        var name = _pendingSave;
        _pendingSave = null;
        Plugin.Guard("Load payroll details", () =>
        {
            Ledger.Load(Sidecar(name), SaveHash(name));
            Plugin.Verbose($"Payroll details loaded: {Ledger.Payments.Count} employee/day entries.");
        });
    }
    private static string Sidecar(string name) => Path.Combine(ModStorage.Root, "HUDOverhaul.payroll",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".json");
    internal static string SaveHash(string name)
    {
        // Validate against the actual save, so a replaced slot or older save
        // cannot acquire a different playthrough's payroll history.
        var path = Path.Combine(Application.persistentDataPath, name + ".sav");
        using var file = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(file));
    }
    internal static void Save(string name)
    {
        EnsureLoaded();
        if (GameHistory.Active) { GameHistory.Save(name); return; }
        Ledger.Save(Sidecar(name), SaveHash(name));
        Plugin.Verbose($"Payroll details saved: {Ledger.Payments.Count} employee/day entries.");
    }
    internal static (long Amount, long Count) Total(VenueAreaGhost venue)
    {
        long amount = 0, count = 0;
        var receipts = venue.Receipts.ReceiptsLookup.GetReceiptsOfType<StaffReceipt>();
        var n = receipts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<StaffReceipt>>().Count;
        for (var i = 0; i < n; i++) { amount += receipts[i].Amount; count += receipts[i].Count; }
        return (amount, count);
    }
}

[HarmonyPatch(typeof(VenueAreaGhost), nameof(VenueAreaGhost.PayStaff))]
internal static class PayrollCapturePatch
{
    [HarmonyPrefix]
    private static void Prefix(VenueAreaGhost __instance, out (long Amount, long Count)? __state)
    {
        (long, long)? before = null;
        Plugin.Guard("Read payroll before payment", () => { Payroll.EnsureLoaded(); before = Payroll.Total(__instance); });
        __state = before;
    }
    [HarmonyPostfix]
    private static void Postfix(VenueAreaGhost __instance, Person staff, (long Amount, long Count)? __state)
    {
        if (__state == null) return;
        var before = __state.Value;
        Plugin.Guard("Record staff payment", () =>
        {
            var after = Payroll.Total(__instance);
            if (after.Count <= before.Count || after.Amount > before.Amount) return;
            Payroll.Ledger.Add(__instance.Venue.Guid, staff.Guid, staff.Name,
                TimeOfDayManager.CurrentTime.GameplayGameDay, checked((int)(after.Count - before.Count)), after.Amount - before.Amount);
            GameHistory.Record(GameHistory.Event("staff_payment",__instance.Venue.Guid,staff.Guid,staff.Name) with
                { Quantity=after.Count-before.Count, Money=after.Amount-before.Amount });
        });
    }
}

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Load))]
internal static class PayrollLoadPatch
{
    [HarmonyPrefix] private static void Prefix(string saveName) => Payroll.BeginLoad(saveName);
}
[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Clear))]
internal static class PayrollClearPatch
{
    [HarmonyPostfix] private static void Postfix() { Payroll.Ledger.Payments.Clear(); GameHistory.Clear(); }
}
[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Save))]
internal static class PayrollSavePatch
{
    [HarmonyPostfix] private static void Postfix(string saveName, bool __result)
    {
        if (__result) Plugin.Guard("Save payroll details", () => Payroll.Save(saveName));
    }
}

internal sealed class PayrollView
{
    internal readonly Dictionary<string, MealSalesTotals.Total> Rows = new();
    internal MealSalesTotals.Total Aggregate = new();
    internal bool Expanded;
    internal bool Adding;
    internal MealSalesTotals.Total? ActiveRow;
    internal int RecordedStaff;
    internal static PayrollView Create(LocaleFinanceOverviewGainsAndCostsPanel panel, int previousFrom, int previousTo, bool compare)
    {
        Payroll.EnsureLoaded();
        var view = new PayrollView { Expanded = panel.costsOnlyToggle.isOn };
        view.Aggregate = new MealSalesTotals.Total { Name = CommonUiStrings.Instance.Staff, Compare = compare,
            InRange = true, CountLabel = "sales.payments" };
        var receipts = panel._venue.RuntimeData.Receipts.ReceiptsLookup.GetReceiptsOfType<StaffReceipt>();
        var n = receipts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<StaffReceipt>>().Count;
        for (var i = 0; i < n; i++)
        {
            var r = receipts[i]; var day = r.Time.GameplayGameDay;
            if (day >= panel._fromDay && day < panel._toDay)
            { view.Aggregate.Current += r.Count; view.Aggregate.Revenue += r.Amount; }
            if (compare && day >= previousFrom && day < previousTo) view.Aggregate.Previous += r.Count;
        }
        foreach (var p in Payroll.Ledger.Payments.Where(p => p.Venue == panel._venue.Guid))
        {
            if (!view.Rows.TryGetValue(p.Person, out var row)) view.Rows[p.Person] = row = new MealSalesTotals.Total
                { Name = p.Name, Compare = compare, CountLabel = "sales.payments" };
            if (p.Day >= panel._fromDay && p.Day < panel._toDay)
            { row.Current += p.Count; row.Revenue += p.Amount; row.InRange = true; }
            if (compare && p.Day >= previousFrom && p.Day < previousTo) row.Previous += p.Count;
        }
        var current = view.Rows.Values.Sum(r => r.Current);
        var previous = view.Rows.Values.Sum(r => r.Previous);
        var amount = view.Rows.Values.Sum(r => r.Revenue);
        // Fail closed if the saved detail disagrees with native receipt totals.
        if (current > view.Aggregate.Current || previous > view.Aggregate.Previous || amount < view.Aggregate.Revenue)
        { view.Rows.Clear(); current = previous = amount = 0; }
        if (current < view.Aggregate.Current || previous < view.Aggregate.Previous || amount != view.Aggregate.Revenue)
            view.Rows[""] = new MealSalesTotals.Total { Name = Labels.Get("sales.unattributedStaff"), InRange = true,
                Current = view.Aggregate.Current - current, Previous = view.Aggregate.Previous - previous,
                Revenue = view.Aggregate.Revenue - amount, Compare = compare, CountLabel = "sales.payments" };
        view.RecordedStaff = view.Rows.Count(pair => pair.Key != "" && pair.Value.Current > 0);
        return view;
    }
    internal MealSalesTotals.Total? Find(string name, int money)
    {
        if (!Adding) return name == Aggregate.Name && money == Aggregate.Revenue ? Aggregate : null;
        return ActiveRow;
    }
}

[HarmonyPatch(typeof(LocaleFinanceOverviewGainsAndCostsPanel), nameof(LocaleFinanceOverviewGainsAndCostsPanel.AddInstance))]
internal static class PayrollRowsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(LocaleFinanceOverviewGainsAndCostsPanel __instance, string name, int price)
    {
        var view = MealSales.Payroll;
        if (view == null || !view.Expanded || view.Adding || name != view.Aggregate.Name || price != view.Aggregate.Revenue) return true;
        view.Adding = true;
        try
        {
            foreach (var row in view.Rows.Values.Where(r => r.Current > 0 || r.Previous > 0 || r.Revenue != 0).OrderBy(r => r.Revenue).ThenBy(r => r.Name))
            {
                view.ActiveRow = row;
                __instance.AddInstance(row.Name, checked((int)row.Revenue));
            }
        }
        finally { view.Adding = false; view.ActiveRow = null; }
        return false;
    }
}
