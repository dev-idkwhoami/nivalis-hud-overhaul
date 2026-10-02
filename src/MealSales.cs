using HarmonyLib;
using Nivalis;
using Nivalis.Locale.UI;
using Nivalis.Player;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisMods.HudOverhaul;

public sealed class MealSales : MonoBehaviour
{
    internal static MealSalesTotals? Current;
    internal static PayrollView? Payroll;
    private LocaleFinanceOverviewGainsAndCostsPanel? _panel;
    private float _nextCheck;
    private int _day;
    private long _fingerprint;
    private IntPtr _venue;
    private readonly Il2CppSystem.Collections.Generic.List<ReceiptBase> _recent = new();
    public MealSales(IntPtr pointer) : base(pointer) { }

    internal void Prepare(LocaleFinanceOverviewGainsAndCostsPanel panel)
    {
        _panel = panel;
        Current = new MealSalesTotals();
        var venue = panel._venue;
        if (venue == null) return;
        var today = TimeOfDayManager.CurrentTime.GameplayGameDay;
        var previousFrom = panel._fromDay;
        var previousTo = panel._fromDay;
        var compare = !panel.lifetimeToggle.isOn;
        if (panel.todayToggle.isOn) previousFrom--;
        else if (panel.thisWeekToggle.isOn) previousFrom -= 7;
        else if (panel.thisMonthToggle.isOn && previousTo > 0)
        {
            // Ask the game's calendar: months need not have equal lengths.
            var now = TimeOfDayManager.CurrentTime;
            var start = now.GetMonth().GetStartTime(now.GameplayGameDay / 365);
            var previous = start.AddSeconds(-86400);
            previousFrom = previous.GetMonth().GetStartTime(previous.GameplayGameDay / 365).GameplayGameDay;
        }
        var receipts = venue.RuntimeData.Receipts.ReceiptsLookup.GetReceiptsOfType<RestaurantReceipt>();
        var count = receipts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<RestaurantReceipt>>().Count;
        var recipes = new Dictionary<string, IRecipe>();
        for (var i = 0; i < count; i++)
        {
            var receipt = receipts[i];
            var meal = receipt.Meal;
            if (meal == null) continue;
            Current.Add(meal.Guid, meal.Name, receipt.Time.GameplayGameDay, receipt.Count,
                receipt.Amount, panel._fromDay, panel._toDay, previousFrom, previousTo, compare);
            var day = receipt.Time.GameplayGameDay;
            if (!(day >= panel._fromDay && day < panel._toDay) &&
                !(compare && day >= previousFrom && day < previousTo)) continue;
            var food = meal.TryCast<FoodItemType>();
            if (food?.RecipeDefinition == null) continue;
            if (!recipes.TryGetValue(meal.Guid, out var recipe))
                recipes[meal.Guid] = recipe = MealDatabase.Instance.GetRuntimeRecipe(food.RecipeDefinition);
            var inputs = recipe.Inputs;
            for (var j = 0; j < inputs.Length; j++)
            {
                var input = inputs[j];
                var item = input.DefaultItem;
                if (item == null) continue;
                // Match the native finance row's cost calculation. Quantities
                // use merged receipt counts; neither value changes game stock.
                Current.Add("ingredient:" + item.Guid, item.Name, receipt.Time.GameplayGameDay,
                    receipt.Count * input.Amount, -item.baseMarketPrice,
                    panel._fromDay, panel._toDay, previousFrom, previousTo, compare);
            }
        }
        Payroll = PayrollView.Create(panel, previousFrom, previousTo, compare);
        _day = today;
        _venue = venue.Pointer;
        _fingerprint = Fingerprint(today);
        _nextCheck = Time.unscaledTime + 1;
    }

    private long Fingerprint(int day)
    {
        var lookup = _panel!._venue.RuntimeData.Receipts.ReceiptsLookup;
        _recent.Clear();
        lookup.GetReceiptsForGameplayDay(day, _recent);
        lookup.GetReceiptsForGameplayDay(day - 1, _recent);
        long hash = 17;
        for (var i = 0; i < _recent.Count; i++)
        {
            var receipt = _recent[i];
            unchecked { hash = hash * 31 + receipt.Count; hash = hash * 31 + receipt.Amount; }
        }
        return hash;
    }

    public void Update()
    {
        if (!ModOptions.Sales.Value || _panel == null || !_panel.gameObject.activeInHierarchy || Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1;
        Plugin.Guard("Refresh meal sales", () =>
        {
            if (_panel._venue == null) return;
            var day = TimeOfDayManager.CurrentTime.GameplayGameDay;
            if (_venue == _panel._venue.Pointer && day == _day && Fingerprint(day) == _fingerprint) return;
            // Native refresh updates revenue and counts together, retaining the
            // selected time/filter controls. Date presets follow day rollover.
            if (_panel.todayToggle.isOn) _panel.DisplayToday();
            else if (_panel.thisWeekToggle.isOn) _panel.DisplayThisWeek();
            else if (_panel.thisMonthToggle.isOn) _panel.DisplayThisMonth();
            else _panel.DisplayLifetime();
        });
    }
}

[HarmonyPatch(typeof(LocaleFinanceOverviewGainsAndCostsPanel), nameof(LocaleFinanceOverviewGainsAndCostsPanel.Show))]
internal static class MealSalesPanelPatch
{
    [HarmonyPrefix]
    private static void Prefix(LocaleFinanceOverviewGainsAndCostsPanel __instance, int fromDay, int toDayExclusive)
    {
        MealSales.Current = null;
        MealSales.Payroll = null;
        if (!ModOptions.Sales.Value) return;
        Plugin.Guard("Read meal sales", () =>
        {
            // Show stores these at entry; our prefix needs the new range too.
            __instance._fromDay = fromDay;
            __instance._toDay = toDayExclusive;
            var helper = __instance.GetComponent<MealSales>() ?? __instance.gameObject.AddComponent<MealSales>();
            helper.Prepare(__instance);
        });
    }
    [HarmonyFinalizer]
    private static void Finalizer() { MealSales.Current = null; MealSales.Payroll = null; }
}

public sealed class MealSalesRow : MonoBehaviour
{
    private LayoutElement? _layout;
    private float _min, _preferred;
    private LayoutElement? _right, _name;
    private float _rightMin, _rightPreferred, _nameMin, _namePreferred;
    private float _costWidth, _rightWidth, _nameWidth;
    private TMPro.TextMeshProUGUI? _text;
    private bool _autoSize, _wrap;
    private float _fontSize, _fontMin, _fontMax;
    public MealSalesRow(IntPtr pointer) : base(pointer) { }
    internal void Restore()
    {
        if (_layout == null) return;
        if (_text != null)
        {
            _text.enableAutoSizing = _autoSize; _text.enableWordWrapping = _wrap;
            _text.fontSizeMin = _fontMin; _text.fontSizeMax = _fontMax; _text.fontSize = _fontSize;
        }
        _layout.minWidth = _min;
        _layout.preferredWidth = _preferred;
        if (_right != null) { _right.minWidth = _rightMin; _right.preferredWidth = _rightPreferred; }
        if (_name != null) { _name.minWidth = _nameMin; _name.preferredWidth = _namePreferred; }
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal void Display(LocaleFinanceOverviewCostListItem row, MealSalesTotals.Total total)
    {
        var text = row.costLabel;
        if (_layout == null)
        {
            _layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            _text = text; _autoSize = text.enableAutoSizing; _wrap = text.enableWordWrapping;
            _fontSize = text.fontSize; _fontMin = text.fontSizeMin; _fontMax = text.fontSizeMax;
            _min = _layout.minWidth;
            _preferred = _layout.preferredWidth;
            _costWidth = text.rectTransform.rect.width;
            _right = text.transform.parent.GetComponent<LayoutElement>();
            _name = row.nameLabel.GetComponent<LayoutElement>();
            if (_right != null)
            {
                _rightMin = _right.minWidth; _rightPreferred = _right.preferredWidth;
                _rightWidth = text.transform.parent.GetComponent<RectTransform>().rect.width;
            }
            if (_name != null)
            {
                _nameMin = _name.minWidth; _namePreferred = _name.preferredWidth;
                _nameWidth = row.nameLabel.rectTransform.rect.width;
            }
        }
        var current = Labels.Get(total.CountLabel ?? "sales.current").Replace("{count}", total.Current.ToString());
        var previous = Labels.Get(total.CountLabel ?? "sales.previous").Replace("{count}", total.Previous.ToString());
        var counts = total.Compare ? $"{current}  <alpha=#88>{previous}<alpha=#FF>" : current;
        text.text = $"{counts}   {text.text}";
        var width = Mathf.Max(_costWidth, text.GetPreferredValues(text.text).x + 8);
        var extra = Mathf.Min(width - _costWidth, Mathf.Max(0, _nameWidth - 120));
        _layout.minWidth = _layout.preferredWidth = _costWidth + extra;
        if (_right != null) _right.minWidth = _right.preferredWidth = _rightWidth + extra;
        if (_name != null)
        {
            _name.minWidth = Mathf.Min(_nameMin, 120);
            _name.preferredWidth = Mathf.Max(120, _nameWidth - extra);
        }
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(12, _fontSize);
        text.fontSizeMax = _fontSize;
        text.enableWordWrapping = false;
    }
}

[HarmonyPatch(typeof(LocaleFinanceOverviewCostListItem), nameof(LocaleFinanceOverviewCostListItem.Display))]
internal static class MealSalesRowPatch
{
    [HarmonyPrefix]
    private static void Prefix(LocaleFinanceOverviewCostListItem __instance) => __instance.GetComponent<MealSalesRow>()?.Restore();
    [HarmonyPostfix]
    private static void Postfix(LocaleFinanceOverviewCostListItem __instance, string costName, int money) => Plugin.Guard("Display meal sales", () =>
    {
        var total = MealSales.Payroll?.Find(costName, money) ?? MealSales.Current?.Find(costName, money);
        if (total == null) return;
        var helper = __instance.GetComponent<MealSalesRow>() ?? __instance.gameObject.AddComponent<MealSalesRow>();
        helper.Display(__instance, total);
        var payroll = MealSales.Payroll;
        if (payroll != null && ReferenceEquals(total, payroll.Aggregate) && payroll.RecordedStaff > 0)
            __instance.nameLabel.text = Labels.Get("sales.staffCount")
                .Replace("{name}", costName).Replace("{count}", payroll.RecordedStaff.ToString());
    });
}
