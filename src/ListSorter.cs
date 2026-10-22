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
    internal Action<SortMode>? ApplyDataOrder;
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
                Plugin.Guard("Apply list order", () =>
                {
                    if (ApplyDataOrder != null) ApplyDataOrder(mode);
                    else throw new InvalidOperationException("No data-order adapter registered for this list.");
                });
            if (Dropdown != null)
                Plugin.Guard("Link sort control navigation", ConnectDropdown);
        }
        finally { PerformanceTrace.End(capture); }
    }

    public void OnDestroy() => Instances.Remove(_id);

    [HideFromIl2Cpp]
    private void ConnectVirtualRow(int index, IItemDisplayUI row)
    {
        if (Dropdown == null || row == null) return;
        var current = row.GameObject.GetComponentInChildren<Selectable>(true);
        if (current == null || !current.isActiveAndEnabled || !current.IsInteractable()) return;
        var above = index > 0 ? _list.GetNavigationTarget(index - 1) : null;
        var below = index + 1 < _list.DataCount ? _list.GetNavigationTarget(index + 1) : null;
        var navigation = current.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnUp = above != null ? above : (Selectable)Dropdown;
        navigation.selectOnDown = below;
        current.navigation = navigation;
        if (above == null)
        {
            var dropdownNavigation = Dropdown.navigation;
            dropdownNavigation.mode = Navigation.Mode.Explicit;
            dropdownNavigation.selectOnDown = current;
            Dropdown.navigation = dropdownNavigation;
        }
    }

    [HideFromIl2Cpp]
    private void ConnectDropdown()
    {
        if (_list._virtualActive)
        {
            // Journal/review rows expose Selectable, not IManualUINavigation.
            // The callback overload is rerun by native virtualization whenever
            // rows are recycled; the Selectable overload requires that interface.
            _list.UpdateUINavigationList(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<
                Il2CppSystem.Action<int, IItemDisplayUI>>(new Action<int, IItemDisplayUI>(ConnectVirtualRow)));
            return;
        }
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

}
