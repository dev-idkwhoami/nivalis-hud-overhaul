using Il2CppInterop.Runtime.Attributes;
using Nivalis.Locale.UI;
using Nivalis.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NivalisMods.HudOverhaul;

public sealed class ReviewMetrics : MonoBehaviour
{
    internal int Time;
    internal int Score;
    internal bool Positive;
    internal bool DateLayoutPrepared;
    internal string? BaseDateText;
    internal string? LastDateDisplay;
    public ReviewMetrics(IntPtr pointer) : base(pointer) { }
}

public sealed class ListSorter : MonoBehaviour
{
    private static readonly Dictionary<int, ListSorter> Instances = new();
    private ItemListUI _list = null!;
    private int _id;
    private bool _dirty;
    internal bool ExternalOrder;
    internal bool SortingControl;
    internal Func<SortMode> Mode = null!;
    internal TMP_Dropdown? Dropdown;

    public ListSorter(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal static ListSorter Get(ItemListUI list)
    {
        var id = list.GetInstanceID();
        if (Instances.TryGetValue(id, out var existing) && existing != null) return existing;
        var sorter = list.gameObject.AddComponent<ListSorter>();
        sorter._list = list;
        sorter._id = id;
        Instances[id] = sorter;
        return sorter;
    }

    [HideFromIl2Cpp]
    internal static void Attach(ItemListUI list, InventoryItemFilteringUi filter)
    {
        if (list == null || filter == null) return;
        var extension = filter.GetComponent<FilterExtension>();
        if (extension == null) return;
        var sorter = Get(list);
        sorter.Mode = extension.Current;
        sorter._dirty = true;
    }

    [HideFromIl2Cpp]
    internal static void MarkDirty(ItemListUI list)
    {
        if (Instances.TryGetValue(list.GetInstanceID(), out var sorter) && sorter != null)
            sorter._dirty = true;
    }

    [HideFromIl2Cpp]
    internal static void RefreshSettings()
    {
        foreach (var sorter in Instances.Values) if (sorter != null) sorter.SyncSettings();
    }
    [HideFromIl2Cpp]
    internal void SyncSettings()
    {
        if (!SortingControl || Dropdown == null) return;
        Dropdown.gameObject.SetActive(ModOptions.Sorting.Value);
        var scroll = _list.GetComponentInParent<ScrollRect>() ?? _list.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null && scroll.transform.parent == Dropdown.transform.parent)
            scroll.GetComponent<RectTransform>().offsetMax = new Vector2(0, ModOptions.Sorting.Value ? -48 : 0);
        if (!ModOptions.Sorting.Value) Dropdown.SetValueWithoutNotify(0);
    }

    public void LateUpdate()
    {
        if (!_dirty || Mode == null || ExternalOrder) return;
        _dirty = false;
        var mode = Mode();
        var capture = PerformanceTrace.Begin($"HUD sort {mode.Label} ({_list.DisplayedCount} rows)");
        try
        {
            if (mode.Key != SortKey.Native)
                Plugin.Guard("Apply list order", () => Apply(mode));
            if (Dropdown != null)
                Plugin.Guard("Link sort control navigation", ConnectDropdown);
        }
        finally { PerformanceTrace.End(capture); }
    }

    public void OnDestroy() => Instances.Remove(_id);

    [HideFromIl2Cpp]
    private void ConnectDropdown()
    {
        // Reviews and journal rows do not implement IManualUINavigation, which
        // the game's list helper requires. Connect their selectables directly.
        var previous = (Selectable)Dropdown!;
        for (var i = 0; i < _list.DisplayedCount; i++)
        {
            var current = _list.GetItem(i).GameObject.GetComponentInChildren<Selectable>(true);
            if (current == null || !current.isActiveAndEnabled || !current.IsInteractable()) continue;
            var priorNavigation = previous.navigation;
            priorNavigation.mode = Navigation.Mode.Explicit;
            priorNavigation.selectOnDown = current;
            previous.navigation = priorNavigation;
            var navigation = current.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = previous;
            current.navigation = navigation;
            previous = current;
        }
        var lastNavigation = previous.navigation;
        lastNavigation.selectOnDown = null;
        previous.navigation = lastNavigation;
    }

    [HideFromIl2Cpp]
    private void Apply(SortMode mode)
    {
        var count = _list.DisplayedCount;
        if (count < 2) return;
        var displays = new IItemDisplayUI[count];
        var transforms = new Transform[count];
        var values = new double?[count];
        var parents = new Dictionary<int, (Transform Parent, Transform[] Children)>();
        for (var i = 0; i < count; i++)
        {
            displays[i] = _list.GetItem(i);
            var go = displays[i].GameObject;
            transforms[i] = go.transform;
            values[i] = Value(go, mode.Key);
            var parent = go.transform.parent;
            if (parent == null) throw new InvalidOperationException("List entry has no layout parent.");
            if (!parents.ContainsKey(parent.GetInstanceID()))
            {
                var children = new Transform[parent.childCount];
                for (var j = 0; j < children.Length; j++) children[j] = parent.GetChild(j);
                parents.Add(parent.GetInstanceID(), (parent, children));
            }
        }

        var order = SortOrder.Indices(values, mode.Descending);
        if (order.Select((source, target) => source == target).All(same => same)) return;

        var up = transforms[0].GetComponentInChildren<Selectable>(true)?.navigation.selectOnUp;
        var down = transforms[^1].GetComponentInChildren<Selectable>(true)?.navigation.selectOnDown;
        if (up != null && transforms.Any(row => up.transform.IsChildOf(row))) up = null;
        if (down != null && transforms.Any(row => down.transform.IsChildOf(row))) down = null;

        var replacements = new Dictionary<int, Transform>();
        for (var i = 0; i < count; i++)
            replacements.Add(transforms[i].GetInstanceID(), transforms[order[i]]);

        // Keep the existing slots, including grid rows, inactive pool entries and
        // layout dummies. Move whole bound UI objects into those slots.
        var layouts = parents.Values.Select(layout => (layout.Parent,
            Children: layout.Children.Select(child => replacements.TryGetValue(child.GetInstanceID(), out var target)
                ? target : child).ToArray())).ToArray();
        foreach (var layout in layouts)
            foreach (var child in layout.Children)
                if (child.parent != layout.Parent) child.SetParent(layout.Parent, false);
        foreach (var layout in layouts)
        {
            for (var i = 0; i < layout.Children.Length; i++) layout.Children[i].SetSiblingIndex(i);
            var rect = layout.Parent.TryCast<RectTransform>();
            if (rect != null) LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
        for (var i = 0; i < count; i++) _list._itemDisplayInstances[i] = displays[order[i]];
        if (Dropdown == null) _list.UpdateUINavigationList(up, down);
    }

    [HideFromIl2Cpp]
    private static double? Value(GameObject go, SortKey key)
    {
        if (key is SortKey.Calories or SortKey.Exclusivity)
        {
            var recipe = go.GetComponent<RecipeItemDisplayUI>()?._displayedRecipe
                ?? go.GetComponent<MenuItemDisplayUI>()?._displayedItem?.Recipe;
            if (recipe == null) return null;
            recipe.CalculateTags(out var calories, out var exclusivity);
            return key == SortKey.Calories ? calories : exclusivity;
        }
        if (key == SortKey.Comfort)
            return go.GetComponent<ShopItemDisplayUI>()?._item?.Type?.Comfort;
        if (key is SortKey.Updated or SortKey.Active)
        {
            var quest = go.GetComponent<JournalListItemUI>()?.Quest;
            if (quest == null) return null;
            return key == SortKey.Updated ? quest.LastUpdateTime : quest.IsActive ? 1 : 0;
        }
        var review = go.GetComponent<ReviewMetrics>();
        if (review == null) return null;
        return key switch {
            SortKey.ReviewTime => review.Time,
            SortKey.Stars => review.Score,
            SortKey.Sentiment => review.Positive ? 1 : 0,
            _ => null
        };
    }
}
