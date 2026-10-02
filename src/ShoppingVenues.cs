using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.UI;
using TMPro;
using TheraBytes.BetterUi;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisMods.HudOverhaul;

public sealed class ShoppingVenues : MonoBehaviour
{
    private ShoppingListPanel _panel = null!;
    private TMP_Dropdown _dropdown = null!;
    private Venue? _selected;
    private List<Venue> _venues = new();
    private bool _applying;
    private ShoppingTabs? _tabs;
    private int _estimateDay;

    public ShoppingVenues(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal static void Refresh(ShoppingListPanel panel)
    {
        var view = panel.GetComponent<ShoppingVenues>() ?? panel.gameObject.AddComponent<ShoppingVenues>();
        if (view._applying) return;
        view._applying = true;
        try
        {
            view._panel = panel;
            view._estimateDay = TimeOfDayManager.CurrentTime.GameplayGameDay;
            view.Prepare();
            view._tabs?.Sync();
            view.UpdateChoices();
            view.Apply();
        }
        finally { view._applying = false; }
    }

    public void Update()
    {
        if (_panel == null || !_panel.IsActive || !EstimatedShopping.Enabled) return;
        if (_estimateDay != TimeOfDayManager.CurrentTime.GameplayGameDay)
            Plugin.Guard("Roll shopping estimate to new day", () => _panel.RefreshItemsList());
    }

    [HideFromIl2Cpp]
    private void Prepare()
    {
        if (_dropdown != null) return;
        SortDropdown.Attach(_panel.itemsList, _panel.scrollRect,
            new[] { new SortMode(Labels.Get("shopping.allVenues"), SortKey.Native) }, Changed, sortingControl: false);
        var sorter = ListSorter.Get(_panel.itemsList);
        _dropdown = sorter.Dropdown!;
        if (_dropdown == null) throw new InvalidOperationException("Shopping venue selector could not be created.");
        _dropdown.gameObject.name = "HUDOverhaul.Venue";
        // Match the venue selector in both shopping list views.
        var background = new Color(0.66f, 0.51f, 0.33f, 1f);
        if (_dropdown.targetGraphic != null) _dropdown.targetGraphic.color = background;
        var templateBackground = _dropdown.template.GetComponent<Image>();
        if (templateBackground != null) templateBackground.color = background;
        var foreground = new Color(0.83f, 0.66f, 0.41f, 1f);
        _dropdown.captionText.color = foreground;
        var optionText = new Color(1f, 0.94f, 0.82f, 1f);
        _dropdown.itemText.color = optionText;
        _dropdown.itemText.canvasRenderer.SetColor(Color.white);
        var toggle = _dropdown.template.GetComponentInChildren<BetterToggle>(true);
        if (toggle != null)
        {
            // BetterToggle applies separate text tints for selection,
            // hover and on/off states, overriding the TMP base color.
            foreach (var transitions in new[] { toggle.BetterTransitions,
                toggle.BetterTransitionsWhenOn, toggle.BetterTransitionsWhenOff,
                toggle.BetterToggleTransitions })
            {
                if (transitions == null) continue;
                foreach (var transition in transitions)
                {
                    var colors = transition.colorTransitions;
                    if (colors == null || colors.target != _dropdown.itemText) continue;
                    colors.colorMultiplier = 1f;
                    foreach (var state in colors.states) state.StateObject = Color.white;
                }
            }
        }
        // Venue selection filters native rows; it is not a sorting mode.
        sorter.Mode = () => new SortMode(Labels.Get("shopping.allVenues"), SortKey.Native);
        _tabs = new ShoppingTabs(_dropdown, _panel.scrollRect);
    }

    [HideFromIl2Cpp]
    private void UpdateChoices()
    {
        var owned = new Il2CppSystem.Collections.Generic.List<Venue>();
        PlayerManager.Instance.LocalPlayer.GetOwnedVenues(owned);
        _venues = owned.ToArray().Where(v => v != null).OrderBy(v => v.EntryName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var selectedIndex = _selected == null ? -1 : _venues.FindIndex(v => v == _selected);
        if (selectedIndex < 0) _selected = null;
        var options = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData(Labels.Get("shopping.allVenues")));
        foreach (var venue in _venues) options.Add(new TMP_Dropdown.OptionData(venue.EntryName));
        _dropdown.options = options;
        _dropdown.SetValueWithoutNotify(selectedIndex + 1);
        _dropdown.RefreshShownValue();
    }

    [HideFromIl2Cpp]
    private void Changed()
    {
        var index = _dropdown.value - 1;
        _selected = index >= 0 && index < _venues.Count ? _venues[index] : null;
        // Let native refresh restore its complete row pool, availability and
        // click callbacks first. The postfix narrows the resulting display only.
        _panel.RefreshItemsList();
        _panel.ScrollToTop();
    }

    [HideFromIl2Cpp]
    private ShoppingListManager.ShoppingListData? Demand(Venue venue, ItemType item)
    {
        var map = EstimatedShopping.Demands != null
            ? EstimatedShopping.Demands : ShoppingListManager.Instance.VenuesLowOnIngredientsMap;
        if (map == null || !map.TryGetValue(venue, out var venueData) || venueData?.LowIngredients == null) return null;
        return venueData.LowIngredients.TryGetValue(item, out var data) ? data : null;
    }

    [HideFromIl2Cpp]
    private string Detail(Venue venue, ShoppingListManager.ShoppingListData demand) =>
        Labels.Get("shopping.venueNeed").Replace("{venue}", venue.EntryName)
            .Replace("{missing}", demand.MissingItems.ToString())
            .Replace("{stock}", demand.amount.ToString()).Replace("{required}", demand.demand.ToString());

    [HideFromIl2Cpp]
    private static float SizeVenueText(TMP_Text text)
    {
        text.enableWordWrapping = true;
        var rect = text.transform.parent.GetComponent<RectTransform>();
        var group = rect.GetComponent<HorizontalLayoutGroup>();
        if (group != null)
        {
            group.CalculateLayoutInputHorizontal();
            group.SetLayoutHorizontal();
        }
        var width = Math.Max(60, text.rectTransform.rect.width);
        var height = Math.Max(35, text.GetPreferredValues(text.text, width, float.PositiveInfinity).y + 10);
        var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return height;
    }

    [HideFromIl2Cpp]
    internal void UpdateRow(ShoppingListItemDisplayUI ui)
    {
        var demands = new List<(Venue Venue, ShoppingListManager.ShoppingListData Data)>();
        foreach (var venue in _venues)
        {
            if (_selected != null && venue != _selected) continue;
            var demand = Demand(venue, ui.ItemType);
            if (demand != null && demand.MissingItems > 0) demands.Add((venue, demand));
        }
        if (EstimatedShopping.Items != null &&
            EstimatedShopping.Items.TryGetValue(ui.ItemType, out var total))
        {
            var carried = PlayerManager.Instance.LocalPlayer.Inventory.Items.GetItemCount(ui.ItemType);
            ui.requiredAmountCounter.text = $"{carried}/{total.demand}";
        }
        // Native initialization restores quest-only rows when the pool is reused.
        if (demands.Count == 0) return;

        // InitializeVenuesInfo overwrites Initialize's counter with the sum of
        // all venue shortages. Override it afterward, retaining carried stock
        // as the numerator (venue stock is already deducted from MissingItems).
        if (_selected != null)
        {
            var carried = PlayerManager.Instance.LocalPlayer.Inventory.Items.GetItemCount(ui.ItemType);
            ui.requiredAmountCounter.text = $"{carried}/{demands[0].Data.MissingItems}";
            // The selector already identifies this venue; remove its entire
            // line (including the icon and reserved layout space).
            ui.allVenuesParent.gameObject.SetActive(false);
            ui.venuesListParent.gameObject.SetActive(false);
            return;
        }

        // The native summary and detail sections can both be visible for one
        // venue. Own one section consistently, including after toggle callbacks.
        var smallText = ui.venuesList.GetComponentInChildren<ShoppingListItemVenueDemand>(true);
        if (smallText != null)
        {
            ui.allVenuesInfo.enableAutoSizing = false;
            ui.allVenuesInfo.fontSize = smallText.venueText.fontSize;
        }
        ui.allVenuesInfo.text = demands.Count == 1 ? demands[0].Venue.EntryName :
            string.Join("\n", demands.Select(d => Detail(d.Venue, d.Data)));
        ui.allVenuesParent.gameObject.SetActive(true);
        ui.venuesListParent.gameObject.SetActive(false);
        SizeVenueText(ui.allVenuesInfo);
    }

    [HideFromIl2Cpp]
    internal static void RestoreRow(ShoppingListItemDisplayUI ui)
    {
        var view = ui.GetComponentInParent<ShoppingVenues>();
        if (view != null && !view._applying) view.UpdateRow(ui);
    }

    [HideFromIl2Cpp]
    private void Apply()
    {
        var list = _panel.itemsList;
        var kept = new List<IItemDisplayUI>();
        var hidden = new List<IItemDisplayUI>();
        var count = list.DisplayedCount;
        for (var i = 0; i < count; i++)
        {
            var display = list.GetItem(i);
            var ui = display.GameObject.GetComponent<ShoppingListItemDisplayUI>();
            if (ui == null) { kept.Add(display); continue; }
            var selectedDemand = _selected == null ? null : Demand(_selected, ui.ItemType);
            if (_selected != null && (selectedDemand == null || selectedDemand.MissingItems <= 0))
            {
                ui.ForceTurnOff();
                hidden.Add(display);
                continue;
            }
            kept.Add(display);
            UpdateRow(ui);
        }
        // Keep the native allocation pool intact, with visible entries first.
        // GetFirstAvailable/IsItemOnList and vendor selection see the filtered set.
        var order = kept.Concat(hidden).ToArray();
        for (var i = 0; i < order.Length; i++) list._itemDisplayInstances[i] = order[i];
        list._displayedInstanceCount = kept.Count;
        list.EndUpdate();
        _panel.SortEntries();
        if (list._itemDisplayParent.TryCast<RectTransform>() is { } rect)
            LayoutRebuilder.MarkLayoutForRebuild(rect);
        if (Plugin.IsVerbose) Plugin.Verbose($"Shopping venues: {(_selected == null ? "all" : _selected.EntryName)}, {kept.Count} items, {_venues.Count} venues.");
    }
}

[HarmonyPatch(typeof(ShoppingListPanel), nameof(ShoppingListPanel.RefreshItemsList))]
internal static class ShoppingVenuePatch
{
    [HarmonyPostfix]
    private static void Postfix(ShoppingListPanel __instance) => Plugin.Guard("Shopping venue filter", () => ShoppingVenues.Refresh(__instance));
}

[HarmonyPatch(typeof(ShoppingListItemDisplayUI), nameof(ShoppingListItemDisplayUI.ToggleAllVenuesInfo))]
internal static class ShoppingVenueDetailsPatch
{
    [HarmonyPostfix]
    private static void Postfix(ShoppingListItemDisplayUI __instance) =>
        Plugin.Guard("Shopping venue details", () => ShoppingVenues.RestoreRow(__instance));
}
