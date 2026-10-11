using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Player;
using NivalisMods.HudOverhaul;
using Data = Nivalis.ShoppingListManager.ShoppingListData;

var item = new ItemType();
var runtime = new RuntimeData();
runtime.Menu.Add(new() { Recipe = new() { Inputs = [new() { DefaultItem = item, Amount = 1 }] } });
var venue = new Venue { RuntimeData = runtime };
PlayerManager.Instance.LocalPlayer.Venues.Add(venue);
EstimatedShopping._enabled = new();
var manager = ShoppingListManager.Instance;
var originalItems = manager.ShoppingList;
var originalVenues = manager.VenuesLowOnIngredientsMap;
var checks = 0;

// No native shortage exists for this stocked item. Ignoring stock must still
// respect Lean/Normal/Extra/recent-sales targets rather than defaulting to zero.
runtime.InventoryData.itemContainer.Stock[item] = 100;
ModOptions.DeductVenueStock.Value = false;
foreach (var target in new[] { 2, 5, 10, 17 })
{
    runtime.Target = target;
    using (EstimatedShopping.Begin())
        Check(manager.ShoppingList[item].demand == target, "native venue target survives absent shortage row");
    Check(ReferenceEquals(manager.ShoppingList, originalItems) &&
        ReferenceEquals(manager.VenuesLowOnIngredientsMap, originalVenues), "scope restores native dictionaries");
}

runtime.Menu.Add(runtime.Menu[0]);
runtime.Target = 10;
using (EstimatedShopping.Begin())
    Check(manager.ShoppingList[item].demand == 20, "shared ingredient accumulates per recipe like native demand");
runtime.Menu.RemoveAt(1);
Check(runtime.Receipts.Reads == 0, "standard mode never scans receipt history");

EstimatedShopping._enabled.Value = true;
runtime.Target = 10;
ModOptions.DeductVenueStock.Value = true;
runtime.InventoryData.itemContainer.Stock[item] = 4;
using (EstimatedShopping.Begin())
    Check(manager.ShoppingList[item].demand == 6, "no-history estimate uses native target and deducts stock");
runtime.InventoryData.itemContainer.Stock[item] = 100;
using (EstimatedShopping.Begin())
    Check(!manager.ShoppingList.ContainsKey(item), "stocked no-history estimate needs no purchase");

runtime.InventoryData.itemContainer.Stock[item] = 0;
PlayerManager.Instance.LocalPlayer.Inventory.Items.Stock[item] = 10;
using (EstimatedShopping.Begin())
    Check(!manager.ShoppingList.ContainsKey(item), "carried inventory satisfies demand");
PlayerManager.Instance.LocalPlayer.Inventory.Items.Stock.Clear();
EstimatedShopping._enabled.Value = false;
Check(EstimatedShopping.Begin() == null && ReferenceEquals(manager.ShoppingList, originalItems),
    "standard mode with stock deduction leaves native shopping untouched");


// Match the game's venue-specific customized recipe resolution, including
// ingredient quantity and output batch size in the seven-day forecast.
EstimatedShopping._enabled.Value = true;
ModOptions.StockDays.Value = 3;
var customItem = new ItemType { Guid = "custom-ingredient" };
var customRecipe = new Recipe { Inputs = [new() { DefaultItem = customItem, Amount = 2 }] };
customRecipe.Output.amount = 2;
customRecipe.Output.type.Guid = "custom-meal";
runtime.Menu[0].VenueRecipe = customRecipe;
runtime.Receipts.History.Add(new() { Meal = customRecipe.Output.type, Count = 70, Time = new() { GameplayGameDay = 7 } });
runtime.Receipts.History.Add(new() { Meal = customRecipe.Output.type, Count = 700, Time = new() { GameplayGameDay = 8 } });
runtime.Receipts.History.Add(new() { Meal = customRecipe.Output.type, Count = 700, Time = new() { GameplayGameDay = 0 } });
foreach (var nativeTarget in new[] { 2, 5, 10, 17 })
{
    runtime.Target = nativeTarget;
    using (EstimatedShopping.Begin())
    {
        Check(manager.ShoppingList[customItem].demand == 30 && !manager.ShoppingList.ContainsKey(item),
            "historical forecast uses completed days, stock days and customized recipe quantities");
        Check(ReferenceEquals(runtime.Menu[0].ResolvedVenue, venue), "recipe resolves for its owning venue");
    }
}

// Keep native quest and equipment requirements while replacing ingredient
// demand; carried inventory is counted once across venues, not once per venue.
runtime.Menu[0].VenueRecipe = null;
runtime.Receipts.History.Clear();
ModOptions.StockDays.Value = 1;
runtime.Target = 2;
var second = new Venue();
second.RuntimeData.Target = 10;
second.RuntimeData.Menu.Add(new() { Recipe = runtime.Menu[0].Recipe });
PlayerManager.Instance.LocalPlayer.Venues.Add(second);
var equipment = new ItemType { Guid = "processor" };
var questOnly = new ItemType { Guid = "quest" };
originalVenues[venue] = new();
originalVenues[venue].LowIngredients[item] = new Data(0, 2);
originalVenues[venue].LowIngredients[equipment] = new Data(0, 1);
originalVenues[second] = new();
originalVenues[second].LowIngredients[item] = new Data(0, 10);
originalItems[item] = new Data(0, 15); // 12 venue + 3 quest
originalItems[equipment] = new Data(0, 1);
originalItems[questOnly] = new Data(0, 4);
PlayerManager.Instance.LocalPlayer.Inventory.Items.Stock[item] = 10;
using (EstimatedShopping.Begin())
{
    Check(manager.ShoppingList[item].demand == 15, "different venue strategies plus shared quest requirement survive");
    Check(manager.ShoppingList[equipment].demand == 1 && manager.ShoppingList[questOnly].demand == 4,
        "equipment and quest-only needs are preserved");
    Check(manager.VenuesLowOnIngredientsMap[venue].LowIngredients[item].demand == 2 &&
        manager.VenuesLowOnIngredientsMap[second].LowIngredients[item].demand == 10,
        "fallback uses each venue's own strategy");
}
EstimatedShopping._enabled.Value = false;
Check(EstimatedShopping.Begin() == null && EstimatedShopping.Items == null && EstimatedShopping.Demands == null,
    "switching back to standard clears custom view and restores native demand");

Console.WriteLine($"Passed {checks} shopping compatibility checks (production estimator with value-type fixtures).");
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
