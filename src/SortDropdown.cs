using Il2CppInterop.Runtime;
using Nivalis.Locale.UI;
using Nivalis.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NivalisMods.HudOverhaul;

internal static class SortDropdown
{
    internal static void Attach(ItemListUI list, ScrollRect scroll, SortMode[] modes, Action refresh, bool sortingControl = true)
    {
        if (list == null || scroll == null || scroll.viewport == null) return;
        var sorter = ListSorter.Get(list);
        sorter.SortingControl = sortingControl;
        if (sorter.Dropdown == null)
        {
            var source = FilterExtension.Template;
            if (source == null)
            {
                foreach (var filter in Resources.FindObjectsOfTypeAll<InventoryItemFilteringUi>())
                {
                    if (filter.sortingDropdown == null) continue;
                    source = filter.sortingDropdown;
                    break;
                }
            }
            if (source == null) throw new InvalidOperationException("No native sorting dropdown template was found.");

            // ScrollRect drives its viewport during layout, overwriting offsets
            // applied there. Reserve space on the whole scroll rectangle instead.
            var scrollRect = scroll.GetComponent<RectTransform>();
            var wrapper = new GameObject("HUDOverhaul.SortableList").AddComponent<RectTransform>();
            wrapper.SetParent(scrollRect.parent, false);
            wrapper.SetSiblingIndex(scrollRect.GetSiblingIndex());
            wrapper.anchorMin = scrollRect.anchorMin;
            wrapper.anchorMax = scrollRect.anchorMax;
            wrapper.pivot = scrollRect.pivot;
            wrapper.sizeDelta = scrollRect.sizeDelta;
            wrapper.anchoredPosition = scrollRect.anchoredPosition;
            wrapper.localScale = scrollRect.localScale;
            wrapper.localRotation = scrollRect.localRotation;
            scrollRect.SetParent(wrapper, false);
            scrollRect.localScale = Vector3.one;
            scrollRect.localRotation = Quaternion.identity;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0, -48);

            var clone = Object.Instantiate(source.gameObject, wrapper);
            clone.name = "HUDOverhaul.Sort";
            var dropdown = clone.GetComponent<TMP_Dropdown>();
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.options = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
            foreach (var mode in modes) dropdown.options.Add(new TMP_Dropdown.OptionData(mode.Label));
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
            dropdown.interactable = true;

            // Reserve a strip inside this list's own rectangle, not over a
            // neighbouring header or tab. Reuse the game's dropdown styling.
            var rect = clone.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(-24, 36);
            rect.anchoredPosition = new Vector2(0, -4);
            var layout = clone.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;

            var navigation = dropdown.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            dropdown.navigation = navigation;
            sorter.Dropdown = dropdown;
            sorter.Mode = () => sortingControl && !ModOptions.Sorting.Value ? new SortMode(Labels.Get("sort.original"), SortKey.Native) : modes[Math.Clamp(dropdown.value, 0, modes.Length - 1)];
            dropdown.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<int>>(
                new Action<int>(_ => Plugin.Guard("Change sorting", refresh))));
            clone.SetActive(true);
            Plugin.Verbose($"Added sorting control to {list.transform.parent?.name}.");
        }
        sorter.SyncSettings();
        ListSorter.MarkDirty(list);
    }
}
