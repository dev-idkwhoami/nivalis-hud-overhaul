using NivalisMods.ModCompanion.Api;
using HarmonyLib;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Player;
using Nivalis.UI;
using Data = Nivalis.ShoppingListManager.ShoppingListData;
using VenueData = Nivalis.ShoppingListManager.VenueLowIngredientsData;
using ItemMap = Il2CppSystem.Collections.Generic.Dictionary<Nivalis.InventorySystem.ItemType, Nivalis.ShoppingListManager.ShoppingListData>;
using VenueMap = Il2CppSystem.Collections.Generic.Dictionary<Nivalis.GhostSystem.CustomerLoop.Venue, Nivalis.ShoppingListManager.VenueLowIngredientsData>;

namespace NivalisMods.HudOverhaul;

internal static class EstimatedShopping
{
    internal static Setting<bool> _enabled = null!;
    private static bool _fallingBack;
    internal static bool Enabled => _enabled.Value;
    internal static VenueMap? Demands;
    internal static ItemMap? Items;
    internal static void Initialize(SettingsCategory shopping)
    {
        _enabled = shopping.Choice("DemandMode", "Shopping demand", false,
            new[] { new Choice<bool>(false, "Standard"), new Choice<bool>(true, "Estimated") },
            "Also remembers the selected shopping tab. Estimates use seven completed days of sales.");
        _enabled.Changed += _ => { if (!_fallingBack) Plugin.Guard("Refresh shopping demand mode", RefreshPanels); };
    }

    internal static void FallBack()
    {
        _fallingBack = true;
        try { _enabled.Value = false; }
        finally { _fallingBack = false; }
        Demands = null; Items = null;
        Plugin.Logger.LogWarning("Estimated shopping unavailable; showing the Standard list.");
    }

    internal static void Select(bool estimated)
    {
        if (_enabled.Value == estimated) return;
        _enabled.Value = estimated;
    }

    internal static void RefreshPanels()
    {
        foreach (var panel in UnityEngine.Resources.FindObjectsOfTypeAll<ShoppingListPanel>())
        {
            if (!panel.gameObject.activeInHierarchy) continue;
            panel.RefreshItemsList();
            panel.ScrollToTop();
        }
    }

    internal sealed class Scope : IDisposable
    {
        private readonly ShoppingListManager _manager;
        private readonly VenueMap _venues;
        private readonly ItemMap _items;
        internal Scope(ShoppingListManager manager, VenueMap venues, ItemMap items)
        {
            _manager = manager; _venues = manager.VenuesLowOnIngredientsMap; _items = manager.ShoppingList;
            manager.VenuesLowOnIngredientsMap = venues;
            manager._ShoppingList_k__BackingField = items;
        }
        public void Dispose()
        {
            _manager.VenuesLowOnIngredientsMap = _venues;
            _manager._ShoppingList_k__BackingField = _items;
        }
    }

    internal static Scope? Begin()
    {
        Demands = null; Items = null;
        if (!Enabled && ModOptions.DeductVenueStock.Value) return null;
        var manager = ShoppingListManager.Instance;
        var nativeVenues = manager.VenuesLowOnIngredientsMap;
        var owned = new Il2CppSystem.Collections.Generic.List<Venue>();
        var player = PlayerManager.Instance.LocalPlayer;
        player.GetOwnedVenues(owned);
        var venues = new VenueMap();
        var totals = new Dictionary<ItemType, int>();
        var replaced = new Dictionary<ItemType, int>();
        var today = TimeOfDayManager.CurrentTime.GameplayGameDay;
        for (var v = 0; v < owned.Count; v++)
        {
            var venue = owned[v];
            var runtime = venue.RuntimeData;
            if (runtime == null) continue;
            nativeVenues.TryGetValue(venue, out var native);
            var sales = new Dictionary<string, long>();
            var history = new HashSet<string>();
            var receipts = runtime.Receipts.ReceiptsLookup.GetReceiptsOfType<RestaurantReceipt>();
            var receiptCount = receipts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<RestaurantReceipt>>().Count;
            for (var r = 0; r < receiptCount; r++)
            {
                var receipt = receipts[r];
                if (receipt.Meal == null) continue;
                var id = receipt.Meal.Guid;
                if (receipt.Time.GameplayGameDay < today) history.Add(id);
                if (DemandEstimate.InHistory(receipt.Time.GameplayGameDay, today))
                    sales[id] = sales.GetValueOrDefault(id) + receipt.Count;
            }
            var ingredients = new Dictionary<ItemType, double>();
            var standardTargets = new Dictionary<ItemType, int>();
            var fallback = new Dictionary<ItemType, int>();
            var menu = runtime.Menu;
            for (var m = 0; m < menu.Count; m++)
            {
                var recipe = menu[m].Recipe;
                if (recipe == null || recipe.Output.type == null) continue;
                var id = recipe.Output.type.Guid;
                var hasHistory = history.Contains(id) && today > 1;
                var daily = ModOptions.StockDays.Value * DemandEstimate.Daily(sales.GetValueOrDefault(id), today) / Math.Max(1, recipe.Output.amount);
                foreach (var input in recipe.Inputs)
                {
                    var item = input.DefaultItem;
                    if (item == null) continue;
                    standardTargets[item] = standardTargets.GetValueOrDefault(item) + ShoppingListManager.demandAmount;
                    ingredients[item] = ingredients.GetValueOrDefault(item) + (hasHistory ? daily * input.Amount : 0);
                    if (!hasHistory && !ModOptions.DeductVenueStock.Value)
                        fallback[item] = fallback.GetValueOrDefault(item) + ShoppingListManager.demandAmount;
                    else if (!hasHistory && native != null && native.LowIngredients.TryGetValue(item, out var standard))
                        fallback[item] = Math.Max(fallback.GetValueOrDefault(item), standard.demand);
                }
            }
            var data = new VenueData { LowIngredients = new ItemMap() };
            // Preserve non-ingredient requirements (appliances/processors).
            if (native != null)
                foreach (var pair in native.LowIngredients)
                {
                    replaced[pair.Key] = replaced.GetValueOrDefault(pair.Key) + pair.Value.MissingItems;
                    if (!ingredients.ContainsKey(pair.Key)) data.LowIngredients[pair.Key] = pair.Value;
                }
            foreach (var pair in ingredients)
            {
                var target = Enabled ? DemandEstimate.Target(pair.Value, fallback.GetValueOrDefault(pair.Key))
                    : standardTargets.GetValueOrDefault(pair.Key);
                var stock = runtime.InventoryData.itemContainer.GetItemCount(pair.Key, out _);
                var needed = DemandEstimate.ShoppingNeed(target, stock, ModOptions.DeductVenueStock.Value);
                if (needed > 0)
                    data.LowIngredients[pair.Key] = new Data { amount = target - needed, demand = target };
            }
            venues[venue] = data;
            foreach (var pair in data.LowIngredients)
                totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value.MissingItems;
        }
        // Native shopping combines venue shortages with other needs. Preserve
        // the remainder (quests etc.) and count carried inventory just once.
        foreach (var pair in manager.ShoppingList)
        {
            var remainder = Math.Max(0, pair.Value.MissingItems - replaced.GetValueOrDefault(pair.Key));
            if (remainder > 0) totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + remainder;
        }
        var items = new ItemMap();
        foreach (var pair in totals)
        {
            var carried = player.Inventory.Items.GetItemCount(pair.Key);
            if (pair.Value > carried) items[pair.Key] = new Data { amount = 0, demand = pair.Value };
        }
        Demands = venues; Items = items;
        return new Scope(manager, venues, items);
    }
}

[HarmonyPatch(typeof(ShoppingListPanel), nameof(ShoppingListPanel.RefreshItemsList))]
internal static class EstimatedShoppingPatch
{
    [HarmonyPrefix]
    private static void Prefix(out EstimatedShopping.Scope? __state)
    {
        EstimatedShopping.Scope? state = null;
        Plugin.Guard("Estimate shopping demand", () => state = EstimatedShopping.Begin());
        if (EstimatedShopping.Enabled && state == null) EstimatedShopping.FallBack();
        __state = state;
    }
    [HarmonyFinalizer]
    private static void Finalizer(EstimatedShopping.Scope? __state) => __state?.Dispose();
}
