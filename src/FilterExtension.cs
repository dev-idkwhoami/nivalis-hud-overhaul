using Nivalis.Locale.UI;
using TMPro;

namespace NivalisMods.HudOverhaul;

// Update #9 owns inventory, shop and recipe sorting. Keep its dropdown as
// the template for the independent journal/review sorting controls.
internal static class FilterExtension
{
    internal static TMP_Dropdown? Template;

    internal static void Prepare(InventoryItemFilteringUi filter)
    {
        SearchDebounce.Attach(filter);
        if (filter.sortingDropdown != null) Template = filter.sortingDropdown;
    }
}
