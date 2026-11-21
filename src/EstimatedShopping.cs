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
    private static bool _scoped;
    internal static bool Enabled => _enabled.Value;
    internal static VenueMap? Demands;
    internal static ItemMap? Items;
    internal static void Initialize(SettingsCategory shopping)
    {
        _enabled = shopping.Choice("DemandMode", "Shopping demand", false,
            new[] { new Choice<bool>(false, "Standard"), new Choice<bool>(true, "Estimated") },
            "Standard follows venue restocking strategies. Estimated uses seven completed days of sales and Stock Days; recipes without sales history use the venue strategy. Venues appear only when this calculation finds a shortage. This changes your shopping list, not manager restocking.");
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

    internal static void RefreshPanels()
    {
        foreach (var panel in UnityEngine.Resources.FindObjectsOfTypeAll<ShoppingListPanel>())
        {
            if (!panel.gameObject.activeInHierarchy) continue;
            var window = panel.GetComponentInParent<ShoppingListWindow>();
            if (window != null) window.RefreshVenueFilter();
            else panel.RefreshItemsList();
            panel.ScrollToTop();
        }
    }

    internal sealed class Scope : IDisposable
    {
        private readonly ShoppingListManager _manager;
        private readonly VenueMap _venues;
        private readonly ItemMap _items;
        private readonly bool _previousScope;
        private bool _disposed;
        internal Scope(ShoppingListManager manager, VenueMap venues, ItemMap items)
        {
            _manager = manager; _venues = manager.VenuesLowOnIngredientsMap; _items = manager.ShoppingList;
            _previousScope = _scoped;
            _scoped = true;
            manager._venuesLowOnIngredientsMap = venues;
            manager._shoppingList = items;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scoped = _previousScope;
            _manager._venuesLowOnIngredientsMap = _venues;
            _manager._shoppingList = _items;
        }
    }

    internal static Scope? Begin()
    {
        // RefreshVenueFilter calls RefreshItemsList, which binds rows. Nested
        // consumers share the outer calculation and restore it on return.
        if (_scoped) return BeginCached();
        Demands = null; Items = null;
        if (!Enabled && ModOptions.DeductVenueStock.Value) return null;
        var manager = ShoppingListManager.Instance;
        var nativeVenues = manager.VenuesLowOnIngredientsMap;
        var owned = new Il2CppSystem.Collections.Generic.List<Venue>();
        var player = PlayerManager.Instance.LocalPlayer;
        player.GetOwnedVenues(owned);
        var venues = new VenueMap();
        var totals = new Dictionary<ItemType, int>();
        var today = TimeOfDayManager.CurrentTime.GameplayGameDay;
        for (var v = 0; v < owned.Count; v++)
        {
            var venue = owned[v];
            var runtime = venue.RuntimeData;
            if (runtime == null) continue;
            nativeVenues.TryGetValue(venue, out var native);
            var sales = new Dictionary<string, long>();
            var history = new HashSet<string>();
            if (Enabled)
            {
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
            }
            var ingredients = new Dictionary<ItemType, double>();
            var standardTargets = new Dictionary<ItemType, int>();
            var fallback = new HashSet<ItemType>();
            var menu = runtime.Menu;
            for (var m = 0; m < menu.Count; m++)
            {
                var recipe = menu[m].GetRecipe(venue);
                if (recipe == null || recipe.Output.type == null) continue;
                var id = recipe.Output.type.Guid;
                var hasHistory = history.Contains(id) && today > 1;
                var daily = ModOptions.StockDays.Value * DemandEstimate.Daily(sales.GetValueOrDefault(id), today) / Math.Max(1, recipe.Output.amount);
                foreach (var input in recipe.Inputs)
                {
                    var item = input.DefaultItem;
                    if (item == null) continue;
                    // Match native demand accumulation, including stocked items that
                    // GetIngredientsDemand omits when "Ignore venue stock" is selected.
                    standardTargets[item] = standardTargets.GetValueOrDefault(item) + runtime.GetSupplyTarget(item);
                    ingredients[item] = ingredients.GetValueOrDefault(item) + (hasHistory ? daily * input.Amount : 0);
                    if (!hasHistory) fallback.Add(item);
                }
            }
            var data = new VenueData { LowIngredients = new ItemMap() };
            // Preserve non-ingredient requirements (appliances/processors).
            if (native != null)
                foreach (var pair in native.LowIngredients)
                {
                    if (!ingredients.ContainsKey(pair.Key)) data.LowIngredients[pair.Key] = pair.Value;
                }
            foreach (var pair in ingredients)
            {
                var standard = standardTargets[pair.Key];
                var target = Enabled ? DemandEstimate.Target(pair.Value, fallback.Contains(pair.Key) ? standard : 0)
                    : standard;
                var stock = runtime.InventoryData.itemContainer.GetItemCount(pair.Key, out _);
                var needed = DemandEstimate.ShoppingNeed(target, stock, ModOptions.DeductVenueStock.Value);
                if (needed > 0)
                    data.LowIngredients[pair.Key] = new Data(target - needed, target);
            }
            venues[venue] = data;
            foreach (var pair in data.LowIngredients)
                totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value.MissingItems;
        }
        // Update #10 exposes quest demand separately. Deriving a remainder
        // from ShoppingList loses quests when carried stock hid the native row.
        foreach (var pair in manager._questItemsDemand)
            totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value;
        var items = new ItemMap();
        foreach (var pair in totals)
        {
            var carried = player.Inventory.Items.GetItemCount(pair.Key);
            if (pair.Value > carried) items[pair.Key] = new Data(0, pair.Value);
        }
        Demands = venues; Items = items;
        return new Scope(manager, venues, items);
    }
    internal static Scope? BeginCached()
    {
        if ((!Enabled && ModOptions.DeductVenueStock.Value) || Demands == null || Items == null) return null;
        return new Scope(ShoppingListManager.Instance, Demands, Items);
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

// The standalone window calculates its native venue counts before refreshing
// the panel. Keep both consumers inside the same temporary demand scope.
[HarmonyPatch(typeof(ShoppingListWindow), nameof(ShoppingListWindow.RefreshVenueFilter))]
internal static class EstimatedShoppingWindowPatch
{
    [HarmonyPrefix]
    private static void Prefix(out EstimatedShopping.Scope? __state)
    {
        EstimatedShopping.Scope? state = null;
        Plugin.Guard("Estimate venue demand", () => state = EstimatedShopping.Begin());
        if (EstimatedShopping.Enabled && state == null) EstimatedShopping.FallBack();
        __state = state;
    }
    [HarmonyFinalizer]
    private static void Finalizer(EstimatedShopping.Scope? __state) => __state?.Dispose();
}

// Recycled and expanded rows read venue details after the refresh scope ends.
// Reuse the last calculation without scanning receipt history for every row.
[HarmonyPatch(typeof(ShoppingListItemDisplayUI), nameof(ShoppingListItemDisplayUI.InitializeVenuesInfo))]
internal static class EstimatedShoppingRowPatch
{
    [HarmonyPrefix]
    private static void Prefix(out EstimatedShopping.Scope? __state) => __state = EstimatedShopping.BeginCached();
    [HarmonyFinalizer]
    private static void Finalizer(EstimatedShopping.Scope? __state) => __state?.Dispose();
}
