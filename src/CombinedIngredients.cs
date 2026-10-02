using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.Localization;
using Nivalis.InventorySystem;
using Nivalis.Locale.UI;
using Nivalis.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NivalisMods.HudOverhaul;

public sealed class CombinedIngredients : MonoBehaviour
{
    private MenuModificationWindow _window = null!;
    private GameObject? _view;
    private UIPanel? _panel;
    private int _showFrame = -1;
    private Button _button = null!;
    private ScrollRect _scroll = null!;
    private TMP_Text _font = null!, _heading = null!;
    private Image? _rowArt;
    private bool _showing;
    public CombinedIngredients(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void Prepare(MenuModificationWindow window)
    {
        if (_view != null) { Return(); _button.gameObject.SetActive(ModOptions.Ingredients.Value); return; }
        _window = window;
        var main = window.transform.Find("MainPanel");

        var source = main.Find("TopInfoPanel/P_StatisticsButton").GetComponent<Button>();
        var staging = new GameObject("HUDOverhaul.IngredientStaging");
        staging.SetActive(false);
        try
        {
            var clone = Object.Instantiate(source.gameObject, staging.transform);
            clone.name = "HUDOverhaul.IngredientsButton";
            NativeUiParts.Relabel(clone, Labels.Get("ingredients.open"));
            _button = clone.GetComponent<Button>();
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() =>
                Plugin.Guard("Open ingredient statistics", Show))));
            clone.transform.SetParent(source.transform.parent, false);
            clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            var buttonLayout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            buttonLayout.minWidth = 140; buttonLayout.preferredWidth = 190; buttonLayout.flexibleWidth = 0;
            clone.SetActive(ModOptions.Ingredients.Value);

            // Clone the Statistics window while inactive so none of its native
            // data controllers initialize. Retain the original frame/layout/close art.
            var stats = window.demographicsPanel;
            _view = Object.Instantiate(stats.gameObject, staging.transform);
            _view.name = "HUDOverhaul.IngredientStatistics";
            _view.SetActive(false);
            Object.DestroyImmediate(_view.GetComponent<DemographicsPanel>());
            // Narrow the framed window, retaining the full-screen dimmer and
            // native vertical layout. Children follow the new width naturally.
            var frame = _view.transform.Find("Wrapper").GetComponent<RectTransform>();
            frame.anchorMin = new Vector2(0.18f, frame.anchorMin.y);
            frame.anchorMax = new Vector2(0.82f, frame.anchorMax.y);
            frame.sizeDelta = new Vector2(-80, frame.sizeDelta.y);
            frame.anchoredPosition = new Vector2(0, frame.anchoredPosition.y);
            var section = _view.transform.Find("Wrapper/StatisticsPanelWrapper");
            var body = section.Find("Wrapper");
            var scrollObject = Object.Instantiate(stats.menuScroll.gameObject, staging.transform);
            var list = scrollObject.GetComponent<ItemListUI>();
            if (list != null) Object.DestroyImmediate(list);
            _scroll = scrollObject.GetComponent<ScrollRect>();
            _scroll.onValueChanged = new ScrollRect.ScrollRectEvent();
            // Statistics-specific sections and period tabs do not apply here.
            for (var i = body.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(body.GetChild(i).gameObject);
            var bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
            if (bodyLayout != null) Object.DestroyImmediate(bodyLayout);
            section.Find("P_TogglesGroup Variant").gameObject.SetActive(false);
            var title = section.Find("TitleWrapper");
            foreach (var localized in title.GetComponentsInChildren<LocalizedStaticUILabel>(true))
                Object.DestroyImmediate(localized);
            _heading = title.GetComponentInChildren<TMP_Text>(true);
            _font = _heading;
            _heading.text = Labels.Get("ingredients.windowTitle");
            _heading.enableAutoSizing = true; _heading.fontSizeMin = 16;
            scrollObject.transform.SetParent(body, false);
            scrollObject.name = "IngredientList";
            var sr = scrollObject.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(20, 16); sr.offsetMax = new Vector2(-20, -96);
            var scrollLayout = scrollObject.GetComponent<LayoutElement>();
            if (scrollLayout != null) scrollLayout.ignoreLayout = true;
            var note = NativeUiParts.Text(body, _font, Labels.Get("ingredients.title"));
            note.fontSize = 20; note.alignment = TextAlignmentOptions.Center;
            note.rectTransform.anchorMin = new Vector2(0, 1);
            note.rectTransform.pivot = new Vector2(.5f, 1);
            note.rectTransform.sizeDelta = new Vector2(-40, 40);
            note.rectTransform.anchoredPosition = new Vector2(0, -42);
            _rowArt = window.menuItemList.ItemPrefab.GetComponent<Image>();
            var group = _view.GetComponent<CanvasGroup>() ?? _view.AddComponent<CanvasGroup>();
            group.alpha = 0; group.interactable = group.blocksRaycasts = false;
            _panel = _view.AddComponent<UIPanel>();
            _panel.requiresMouse = _panel.pauseTimeWhenOpen = _panel.pauseTimeCompletely = true;
            _panel._startVisible = false;
            _panel.closeWithCancel = _panel.closeWithPause = true;
            _panel.OnHideEvent += (Il2CppSystem.Action)(() => { _showing = false; _showFrame = -1; });
            var close = section.Find("P_Element_CloseBtn/CloseBtn").GetComponent<Button>();
            close.onClick = new Button.ButtonClickedEvent();
            close.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(Return)));
            _panel.firstSelected = close.gameObject;
            _view.transform.SetParent(UIManager.Instance.FirstChildCanvas.transform, false);
            _view.SetActive(true);
            ClearRows();
            scrollObject.SetActive(true);
        }
        finally { Object.Destroy(staging); }
    }

    [HideFromIl2Cpp]
    private void ClearRows()
    {
        for (var i = _scroll.content.childCount - 1; i >= 0; i--)
        { var child = _scroll.content.GetChild(i).gameObject; child.SetActive(false); Object.Destroy(child); }
    }
    [HideFromIl2Cpp]
    internal void Refresh()
    {
        if (!_showing) return;
        var totals = new Dictionary<string, (ItemType Item, int Count, Dictionary<int, (string Name, int Count)> Recipes)>();
        var menu = _window._openedForVenueMenu;
        if (menu != null)
            for (var i = 0; i < menu.Count; i++)
            {
                var recipe = menu[i].Recipe;
                if (recipe == null) continue;
                foreach (var input in recipe.Inputs)
                {
                    var item = input.DefaultItem;
                    if (item == null || input.Amount <= 0) continue;
                    totals.TryGetValue(item.Guid, out var old);
                    var recipes = old.Recipes ?? new Dictionary<int, (string Name, int Count)>();
                    recipes.TryGetValue(i, out var contribution);
                    recipes[i] = (recipe.Output.type.Name, checked(contribution.Count + input.Amount));
                    totals[item.Guid] = (item, checked(old.Count + input.Amount), recipes);
                }
            }
        ClearRows();
        _heading.text = Labels.Get(totals.Count == 0 ? "ingredients.empty" : "ingredients.windowTitle");
        Canvas.ForceUpdateCanvases();
        foreach (var entry in totals.Values.OrderBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = NativeUiParts.Rect("Ingredient", _scroll.content);
            NativeUiParts.Image(_rowArt, row.gameObject);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.flexibleHeight = 0;
            var iconRect = NativeUiParts.Rect("Icon", row);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, 1);
            iconRect.pivot = new Vector2(0, 1); iconRect.anchoredPosition = new Vector2(12, -10);
            iconRect.sizeDelta = new Vector2(54, 54);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = entry.Item.Icon; icon.preserveAspect = true; icon.raycastTarget = false;
            var name = NativeUiParts.Text(row, _font, entry.Item.Name);
            name.fontSize = 24; name.rectTransform.anchorMin = new Vector2(0, 1);
            name.rectTransform.offsetMin = new Vector2(80, -40);
            name.rectTransform.offsetMax = new Vector2(-100, -6);
            var count = NativeUiParts.Text(row, _font, "×" + entry.Count);
            count.fontSize = 24; count.alignment = TextAlignmentOptions.MidlineRight;
            count.rectTransform.anchorMin = new Vector2(1, 1);
            count.rectTransform.offsetMin = new Vector2(-96, -40); count.rectTransform.offsetMax = new Vector2(-18, -6);
            var text = string.Join("\n", entry.Recipes.Values.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => r.Name));
            var detail = NativeUiParts.Text(row, _font, text);
            detail.fontSize = 20; detail.enableWordWrapping = true;
            detail.alignment = TextAlignmentOptions.TopLeft; detail.overflowMode = TextOverflowModes.Overflow;
            var height = detail.GetPreferredValues(text, Mathf.Max(100, _scroll.viewport.rect.width - 110), float.PositiveInfinity).y;
            detail.rectTransform.anchorMin = new Vector2(0, 1);
            detail.rectTransform.offsetMin = new Vector2(80, -44 - height);
            detail.rectTransform.offsetMax = new Vector2(-24, -44);
            layout.minHeight = layout.preferredHeight = Mathf.Max(80, 56 + height);
        }
        Canvas.ForceUpdateCanvases();
        _scroll.verticalNormalizedPosition = 1;
    }
    [HideFromIl2Cpp]
    private void Show()
    {
        _showing = true;
        _view!.transform.SetAsLastSibling();
        Refresh();
        // Let a newly created native UIPanel complete Start before Show.
        _showFrame = Time.frameCount + 1;
    }
    public void Update()
    {
        if (_showFrame < 0 || Time.frameCount < _showFrame) return;
        _showFrame = -1;
        Plugin.Guard("Show ingredient statistics", () => _panel!.Show());
    }
    [HideFromIl2Cpp]
    private void Return()
    {
        _showFrame = -1; _showing = false;
        if (_panel != null && _panel.IsVisible) _panel.Hide();
    }
    public void OnDisable() => Return();
    public void OnDestroy() { if (_view != null) Object.Destroy(_view); }

}

[HarmonyPatch(typeof(MenuModificationWindow), nameof(MenuModificationWindow.Initialize))]
internal static class CombinedIngredientsPreparePatch
{
    [HarmonyPostfix] private static void Postfix(MenuModificationWindow __instance) => Plugin.Guard("Prepare ingredient view", () =>
        (__instance.GetComponent<CombinedIngredients>() ?? __instance.gameObject.AddComponent<CombinedIngredients>()).Prepare(__instance));
}
[HarmonyPatch(typeof(MenuModificationWindow), nameof(MenuModificationWindow.RefreshMenuList))]
internal static class CombinedIngredientsRefreshPatch
{
    [HarmonyPostfix] private static void Postfix(MenuModificationWindow __instance) => Plugin.Guard("Refresh ingredient totals", () =>
        __instance.GetComponent<CombinedIngredients>()?.Refresh());
}
