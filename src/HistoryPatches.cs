using System.Reflection;
using HarmonyLib;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Locale;
using Nivalis.Locale.UI;
using Nivalis.Player;

namespace NivalisMods.HudOverhaul;

[HarmonyPatch(typeof(MenuItemDisplayUI), nameof(MenuItemDisplayUI.ChangePrice))]
internal static class HistoryPricePatch
{
    internal sealed record Before(MealMenuItem Item, string Venue, string Id, string Name, int Price);
    [HarmonyPrefix]
    private static void Prefix(MenuItemDisplayUI __instance, out Before? __state)
    {
        Before? value = null;
        GameHistory.Safe("price before", () =>
        {
            if (!GameHistory.Active || !GameHistory.Owned(__instance._venue)) return;
            var item = __instance._displayedItem; var meal = item?.Meal;
            if (meal != null) value = new Before(item!, __instance._venue.Guid, meal.Guid, meal.Name, item!.Price);
        });
        __state = value;
    }
    [HarmonyPostfix]
    private static void Postfix(Before? __state)
    {
        if (__state is not { } before) return;
        GameHistory.Safe("price after", () => GameHistory.Change("meal_price", before.Venue, before.Id, before.Name, before.Price, before.Item.Price, "MenuItemDisplayUI.ChangePrice"));
    }
}

[HarmonyPatch]
internal static class HistoryWagePatch
{
    internal sealed record Before(Person Person, string Venue, int Wage);
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LocaleStaffDetailsDisplay), nameof(LocaleStaffDetailsDisplay.IncreaseWage));
        yield return AccessTools.Method(typeof(LocaleStaffDetailsDisplay), nameof(LocaleStaffDetailsDisplay.DecreaseWage));
        yield return AccessTools.Method(typeof(VenueStaffListItem), nameof(VenueStaffListItem.WageIncreaseListener));
        yield return AccessTools.Method(typeof(VenueStaffListItem), nameof(VenueStaffListItem.WageDecreaseListener));
    }
    // WorksAtVenue can be null even for a hired employee. Resolve against the
    // current owned rosters on each click, avoiding stale mappings after transfers.
    private static Venue? FindEmployer(Person person)
    {
        var owned = new Il2CppSystem.Collections.Generic.List<Venue>();
        Nivalis.PlayerManager.Instance.LocalPlayer.GetOwnedVenues(owned);
        Venue? found = null;
        for (var i = 0; i < owned.Count; i++)
        {
            var venue = owned[i];
            if (!GameHistory.Owned(venue) || venue.RuntimeData == null) continue;
            var staff = venue.RuntimeData.Staff;
            if (staff == null) continue;
            var count = staff.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Person>>().Count;
            for (var j = 0; j < count; j++)
            {
                if (staff[j] == null || staff[j].Guid != person.Guid) continue;
                if (found != null && found.Guid != venue.Guid)
                {
                    return null;
                }
                found = venue;
                break;
            }
        }
        return found;
    }

    [HarmonyPrefix]
    private static void Prefix(object __instance, MethodBase __originalMethod, out Before? __state)
    {
        Before? value = null;
        var source = __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name;
        GameHistory.Safe("wage before " + source, () =>
        {
            if (!GameHistory.Active) return;
            var person = __instance is LocaleStaffDetailsDisplay details ? details._person : (__instance as VenueStaffListItem)?._person;
            if (person == null) return;
            var runtime = person.RuntimeData;
            if (runtime == null) return;
            var venue = FindEmployer(person);
            if (!GameHistory.Owned(venue)) return;
            value = new Before(person, venue!.Guid, runtime.Wage);
        });
        __state = value;
    }
    [HarmonyPostfix]
    private static void Postfix(Before? __state, MethodBase __originalMethod)
    {
        var source = __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name;
        GameHistory.Safe("wage after " + source, () =>
        {
            if (__state is not { } before) return;
            var after = before.Person.RuntimeData.Wage;
            GameHistory.Change("staff_wage", before.Venue, before.Person.Guid, before.Person.Name, before.Wage, after, source);
        });
    }
    [HarmonyFinalizer]
    private static void Finalizer(Exception? __exception, MethodBase __originalMethod)
    {
        if (__exception != null)
            Plugin.Logger.LogError($"History wage callback threw in {__originalMethod.DeclaringType!.Name}.{__originalMethod.Name}: {__exception}");
    }

}

// Init occurs before receipts merge, preserving the actual price and timestamp.
// Commit only after the enclosing purchase path completes normally.
internal sealed class SaleCapture
{
    internal static SaleCapture? Current;
    internal readonly SaleCapture? Parent = Current;
    internal readonly List<HistoryEvent> Events = new();
    internal readonly Venue? Venue;
    internal SaleCapture(Venue? venue) { Venue = venue; Current = this; }
    internal void Finish(Exception? error)
    {
        Current = Parent;
        if (error == null) foreach (var e in Events) GameHistory.Record(e);
    }
}
[HarmonyPatch(typeof(VisitVenueAgentActionType.LeaveVenueSubAction), nameof(VisitVenueAgentActionType.LeaveVenueSubAction.LeaveReview))]
internal static class HistoryRestaurantScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(VisitVenueAgentActionType.State state, out SaleCapture? __state)
        => __state = GameHistory.Active && GameHistory.Owned(state.Venue) ? new SaleCapture(state.Venue) : null;
    [HarmonyFinalizer] private static void Finalizer(SaleCapture? __state, Exception? __exception) => __state?.Finish(__exception);
}
[HarmonyPatch(typeof(VisitVendingAgentActionType.BuyAtMachine), nameof(VisitVendingAgentActionType.BuyAtMachine.Tick))]
internal static class HistoryVendingScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(VisitVendingAgentActionType.State state, out SaleCapture? __state)
        => __state = GameHistory.Active && GameHistory.Owned(state.Venue) ? new SaleCapture(state.Venue) : null;
    [HarmonyFinalizer] private static void Finalizer(SaleCapture? __state, Exception? __exception) => __state?.Finish(__exception);
}
[HarmonyPatch(typeof(RestaurantReceipt), nameof(RestaurantReceipt.Init))]
internal static class HistorySalePatch
{
    [HarmonyPostfix]
    private static void Postfix(RestaurantReceipt __instance) => GameHistory.Safe("sale", () =>
    {
        var scope = SaleCapture.Current;
        if (scope == null || !GameHistory.Owned(scope.Venue) || __instance.Meal == null) return;
        var item = __instance.Meal;
        scope.Events.Add(GameHistory.Event("meal_sale", scope.Venue!.Guid, item.Guid, item.Name) with
        { Quantity = __instance.Count, Money = __instance.Amount, Source = "purchase_receipt_before_merge" });
    });
}
[HarmonyPatch(typeof(VenueInventory), nameof(VenueInventory.ContentsChangedListener))]
internal static class HistoryStockPatch
{
    [HarmonyPostfix]
    private static void Postfix(VenueInventory __instance, ItemTypeCountChange change)
        => GameHistory.Safe("stock", () => GameHistory.StockChange(__instance, change));
}

[HarmonyPatch]
internal static class HistoryPurchasePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Nivalis.PlayerManager.Player), nameof(Nivalis.PlayerManager.Player.TryMakePurchase));
        yield return AccessTools.Method(typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryMakePurchase));
    }
    [HarmonyPrefix]
    private static void Prefix(object __instance, ItemType __1, int __2,
        Il2CppSystem.Collections.Generic.List<ItemInstanceData> __3, out HistoryEvent? __state)
    {
        HistoryEvent? value = null;
        GameHistory.Safe("purchase before", () =>
        {
            if (!GameHistory.Active || __1 == null || __3 == null) return;
            var venue = __instance as VenueAreaGhost;
            if (venue != null && !GameHistory.Owned(venue.Venue)) return;
            value = GameHistory.Event("item_purchase", venue?.Venue.Guid ?? "", __1.Guid, __1.Name) with
            { Quantity = __3.Count, Money = -(long)__2, Source = venue == null ? "player_purchase" : "venue_purchase" };
        });
        __state = value;
    }
    [HarmonyPostfix]
    private static void Postfix(HistoryEvent? __state, bool __result)
    { if (__result && __state != null) GameHistory.Record(__state); }
}
