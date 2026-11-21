using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.UI;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Venue selection, counts, row details and shop foldouts belong to the game.
// This component only refreshes estimates across a day boundary; it creates no UI.
public sealed class ShoppingView : MonoBehaviour
{
    private ShoppingListPanel _panel = null!;
    private int _estimateDay;
    public ShoppingView(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal static void Refresh(ShoppingListPanel panel)
    {
        var view = panel.GetComponent<ShoppingView>() ?? panel.gameObject.AddComponent<ShoppingView>();
        view._panel = panel;
        view._estimateDay = TimeOfDayManager.CurrentTime.GameplayGameDay;
    }

    public void Update()
    {
        if (_panel == null || !_panel.IsActive || !EstimatedShopping.Enabled) return;
        if (_estimateDay != TimeOfDayManager.CurrentTime.GameplayGameDay)
            Plugin.Guard("Roll shopping estimate to new day", EstimatedShopping.RefreshPanels);
    }
}

[HarmonyPatch(typeof(ShoppingListPanel), nameof(ShoppingListPanel.RefreshItemsList))]
internal static class ShoppingViewPatch
{
    [HarmonyPostfix]
    private static void Postfix(ShoppingListPanel __instance) =>
        Plugin.Guard("Shopping estimate day tracking", () => ShoppingView.Refresh(__instance));
}
