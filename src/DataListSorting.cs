using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using Nivalis.UI;
using MenuEntry = Il2CppSystem.ValueTuple<Nivalis.Locale.MealMenuItem, Nivalis.Locale.UI.MenuItemDisplayUI.WarningType>;

namespace NivalisMods.HudOverhaul;

// Sort complete native data, never the subset of UI objects currently realized.
internal static class DataListSorting
{
    internal static void Journal(JournalUI view, SortMode mode) =>
        Apply<RuntimeQuest, JournalListItemUI>(view.entriesListUi, view._quests, mode, quest => mode.Key == SortKey.Updated
                ? quest.LastUpdateTime : quest.IsActive ? 1 : 0);

    internal static void Vendor(ShopUiVendorPanel view, SortMode mode) =>
        Apply<ShopUiVendorPanel.ShopEntry, ShopItemDisplayUI>(view.itemsList, view._entries, mode, entry => entry.Item.Type?.Comfort);

    internal static void Recipes(MenuModificationWindow view, SortMode mode) =>
        Apply<IRecipe, RecipeItemDisplayUI>(view.recipeItemList, view._listedRecipes, mode, recipe => RecipeValue(recipe, mode.Key));

    internal static void Menu(MenuModificationWindow view, SortMode mode) =>
        Apply<MenuEntry, MenuItemDisplayUI>(view.menuItemList, view._listedMenuItems, mode,
            entry => RecipeValue(entry.Item1.GetRecipe(view._venue), mode.Key));

    internal static void Reviews(LocaleReviewOverviewTab view, SortMode mode) =>
        Apply<Review, ReviewItemDisplayUi>(view.reviewList, view._listedReviews, mode, review => mode.Key switch
            {
                SortKey.ReviewTime => review.ReviewTime.TotalGameSeconds,
                SortKey.Stars => review.Score,
                SortKey.Sentiment => review.Positive ? 1 : 0,
                _ => null
            });

    private static double? RecipeValue(IRecipe recipe, SortKey key)
    {
        if (recipe == null) return null;
        recipe.CalculateTags(out var calories, out var exclusivity);
        return key == SortKey.Calories ? calories : exclusivity;
    }

    private static void Apply<TData, TView>(ItemListUI list,
        Il2CppSystem.Collections.Generic.List<TData> data, SortMode mode, Func<TData, double?> value)
        where TView : class
    {
        if (data == null || data.Count < 2 || mode.Key == SortKey.Native) return;
        var snapshot = Enumerable.Range(0, data.Count).Select(i => data[i]).ToArray();
        var order = SortOrder.Indices(snapshot.Select(value).ToArray(), mode.Descending);
        if (order.Select((source, target) => source == target).All(same => same)) return;
        Replace<TData, TView>(list, data, snapshot, order);
    }

    internal static void Replace<TData, TView>(ItemListUI list,
        Il2CppSystem.Collections.Generic.List<TData> data, TData[] snapshot, int[] order)
        where TView : class
    {
        // Preserve the exact callback constructed by the native refresh,
        // including its IL2CPP value-type invocation metadata. Reconstructing a
        // delegate by method name is unnecessary and can change that contract.
        var context = list._binder?.Target?.TryCast<ItemListUI.__c__DisplayClass101_0<TData, TView>>();
        var callback = context?.bind ?? throw new InvalidOperationException("Native list binding context is unavailable.");
        var selected = SortOrder.RemapSelection(order, list.SelectedIndex);
        data.Clear();
        foreach (var source in order) data.Add(snapshot[source]);
        list.SetItems<TData, TView>(data.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<TData>>(), callback, selected);
    }
}
