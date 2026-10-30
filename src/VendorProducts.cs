using HarmonyLib;
using Nivalis.GhostSystem.Ai;
using Nivalis.Locale.UI;
using Nivalis.UI.InGameMenu.CharacterWindow;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Use the native product filter and binder so prices, stock and controller rows
// stay game-owned. Native name matches otherwise suppress the product filter.
[HarmonyPatch(typeof(VendorDetails), nameof(VendorDetails.Initialize))]
internal static class VendorProducts
{
    [HarmonyPrefix]
    private static void Prefix(VendorDetails __instance, Person __0, ref string? __1)
    {
        if (!ModOptions.Search.Value) return;
        try
        {
            var window = __instance.GetComponentInParent<CharacterWindowUi>(true);
            if (window == null || window.vendorCharactersToggle?.isOn != true) return;
            var query = window.vendorFilteringUI?.searchField?.text?.Trim();
            if (string.IsNullOrEmpty(query)) { __1 = null; return; }
            var items = __0?.VendorDefinition?.items;
            if (items == null) return;
            foreach (var item in items.Values)
            {
                if (item?.ItemType == null || !VendorFilteringUI.TextMatches(item.ItemType.Name, query)) continue;
                __1 = query;
                return;
            }
            // A vendor-name-only search should still show that vendor's catalogue.
            if (__0 != null && VendorFilteringUI.TextMatches(__0.Name, query)) __1 = null;
        }
        catch (Exception e) { Plugin.Logger.LogError($"Vendor product search failed: {e}"); }
    }

    internal static void Refresh(CharacterWindowUi window)
    {
        if (window == null || !window.isActiveAndEnabled || window.vendorCharactersToggle?.isOn != true) return;
        var details = window.vendorDetails;
        if (details == null || details._person == null || details.canvasGroup == null || details.canvasGroup.alpha <= 0) return;
        details.Initialize(details._person, window.vendorFilteringUI.GetProductsFilter(details._person));
        var scroll = window.vendorProductsListScrollRect;
        if (scroll != null)
        {
            scroll.velocity = Vector2.zero;
            scroll.verticalNormalizedPosition = 1f;
        }
    }

    internal static void RefreshOpenWindows()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<CharacterWindowUi>()) Refresh(window);
    }
}

// Updating a query can retain the same selected vendor, without a new click.
[HarmonyPatch(typeof(CharacterWindowUi), nameof(CharacterWindowUi.RefreshCharacterList))]
internal static class VendorProductsRefresh
{
    [HarmonyPostfix]
    private static void Postfix(CharacterWindowUi __instance)
    {
        if (ModOptions.Search.Value) Plugin.Guard("Refresh vendor products", () => VendorProducts.Refresh(__instance));
    }
}
