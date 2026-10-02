using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Nivalis.Locale.UI;
using Nivalis.UI;
using TMPro;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

public sealed class FilterExtension : MonoBehaviour
{
    private InventoryItemFilteringUi _filter = null!;
    private readonly Dictionary<int, SortMode> _custom = new();
    private bool _prepared;
    private List<InventoryItemFilteringUi.SortingPair>? _original;
    internal static TMP_Dropdown? Template;

    public FilterExtension(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal static void Prepare(InventoryItemFilteringUi filter)
    {
        SearchDebounce.Attach(filter);
        if (filter.sortingDropdown == null || filter.sortingValues == null) return;
        Template = filter.sortingDropdown;
        var extension = filter.GetComponent<FilterExtension>() ?? filter.gameObject.AddComponent<FilterExtension>();
        extension._filter = filter;
        if (extension._prepared || !ModOptions.Sorting.Value) return;

        var pairs = Enumerable.Range(0, filter.sortingValues.Length).Select(i => NativeSortingPairs.Read(filter.sortingValues, i)).ToList();
        extension._original ??= pairs.ToList();
        var menu = Ancestor<MenuModificationWindow>(filter.transform);
        // The shop's filter is a sibling of its two lists, on their shared window.
        var vendor = Ancestor<ShopUINew>(filter.transform);

        void Native(ListSortingOption key, bool descending)
        {
            if (!pairs.Any(pair => pair.Ordering == key && pair.Descending == descending))
                pairs.Add(new InventoryItemFilteringUi.SortingPair { Ordering = key, Descending = descending });
        }
        void Custom(SortMode mode)
        {
            extension._custom.Add(pairs.Count, mode);
            // Native filtering still runs safely with a valid, supported enum.
            // The display list receives the additional order after binding.
            pairs.Add(new InventoryItemFilteringUi.SortingPair { Ordering = ListSortingOption.Alphabetical });
        }

        Native(ListSortingOption.Price, false);
        Native(ListSortingOption.Price, true);
        if (menu == null)
        {
            Native(ListSortingOption.Count, false);
            Native(ListSortingOption.Count, true);
        }
        if (menu != null)
        {
            Custom(new(Labels.Get("sort.calories.asc"), SortKey.Calories));
            Custom(new(Labels.Get("sort.calories.desc"), SortKey.Calories, true));
            Custom(new(Labels.Get("sort.exclusivity.asc"), SortKey.Exclusivity));
            Custom(new(Labels.Get("sort.exclusivity.desc"), SortKey.Exclusivity, true));
        }
        if (vendor != null)
        {
            Custom(new(Labels.Get("sort.comfort.desc"), SortKey.Comfort, true));
            Custom(new(Labels.Get("sort.comfort.asc"), SortKey.Comfort));
        }
        filter.sortingValues = NativeSortingPairs.Create(pairs);
        extension._prepared = true;
        if (Plugin.IsVerbose) Plugin.Verbose($"Sorting pair native stride: {NativeSortingPairs.Stride(filter.sortingValues)}; pairs: {string.Join(", ", pairs.Select(p => $"{p.Ordering}/{p.Descending}"))}");
        if (Plugin.IsVerbose) Plugin.Verbose($"Expanded sorting dropdown: {filter.transform.parent?.name} ({pairs.Count} options).");
    }

    [HideFromIl2Cpp]
    internal static void RefreshSettings()
    {
        foreach (var extension in Resources.FindObjectsOfTypeAll<FilterExtension>())
        {
            if (extension._filter == null) continue;
            if (extension._original == null) { extension._filter.CreateOptions(); continue; }
            extension._filter.sortingValues = NativeSortingPairs.Create(extension._original);
            extension._custom.Clear(); extension._prepared = false;
            extension._filter.sortingDropdown.SetValueWithoutNotify(0);
            extension._filter.CreateOptions();
        }
        ListSorter.RefreshSettings();
    }

    [HideFromIl2Cpp]
    internal void Relabel()
    {
        var dropdown = _filter.sortingDropdown;
        // The game's label lookup does not cover every supported sorting enum.
        // Label every pair, not only the custom entries (which use a native
        // alphabetical fallback for filtering).
        for (var index = 0; index < dropdown.options.Count && index < _filter.sortingValues.Length; index++)
        {
            var pair = NativeSortingPairs.Read(_filter.sortingValues, index);
            var label = _custom.TryGetValue(index, out var mode) ? mode.Label : pair.Ordering switch
            {
                ListSortingOption.Alphabetical => pair.Descending ? Labels.Get("sort.name.desc") : Labels.Get("sort.name.asc"),
                ListSortingOption.Price => pair.Descending ? Labels.Get("sort.price.desc") : Labels.Get("sort.price.asc"),
                ListSortingOption.Count => pair.Descending ? Labels.Get("sort.count.desc") : Labels.Get("sort.count.asc"),
                ListSortingOption.LastUpdatedTime => pair.Descending ? Labels.Get("sort.updated.desc") : Labels.Get("sort.updated.asc"),
                ListSortingOption.Usage => pair.Descending ? Labels.Get("sort.usage.desc") : Labels.Get("sort.usage.asc"),
                _ => pair.Ordering.ToString()
            };
            dropdown.options[index].text = label;
            // Native arrows describe the fallback order for custom entries.
            // Direction is explicit in every label, so remove misleading icons.
            dropdown.options[index].image = null;
        }
        dropdown.RefreshShownValue();
    }

    [HideFromIl2Cpp]
    internal SortMode Current() => _custom.TryGetValue(_filter.sortingDropdown.value, out var mode)
        ? mode : new SortMode(Labels.Get("sort.original"), SortKey.Native);

    [HideFromIl2Cpp]
    private static T? Ancestor<T>(Transform current) where T : Component
    {
        // This Unity version lacks the generic includeInactive parent overload.
        while (current != null)
        {
            var component = current.GetComponent<T>();
            if (component != null) return component;
            current = current.parent;
        }
        return null;
    }
}
