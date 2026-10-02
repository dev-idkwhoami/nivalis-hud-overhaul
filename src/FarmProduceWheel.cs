using HarmonyLib;
using Nivalis.InventorySystem;
using Nivalis.UI;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Reuse the native farming/quick-action wheel, with label-only callbacks.
internal static class FarmProduceWheel
{
    private static RadialMenuUI? _wheel;
    private static Sprite? _removeIcon;
    private static FarmScreen? _screen;
    private static string? _identity;

    internal static void Open(FarmScreen screen)
    {
        var wheel = RadialMenuUI.instance;
        if (wheel == null || wheel.IsOpen || !screen.CanUse()) return;
        var identity = FarmScreen.TargetKey(screen.Module);
        wheel.Clear();
        try
        {
            foreach (var produce in screen.Choices())
            {
                var selected = produce;
                wheel.AddAction(selected.Icon, selected.Name, false,
                    (Il2CppSystem.Action)(() => Plugin.Guard("Set farm screen target",
                        () => Save(screen, identity, selected))), true);
            }
            wheel.AddAction(screen.Module.MyGhost.ModuleType.Icon, Labels.Get("farm.autoMode"), false,
                (Il2CppSystem.Action)(() => Plugin.Guard("Enable automatic farm label",
                    () => Save(screen, identity, null, true))), true);
            // Use the inventory's trash bin, not the adjacent close action's X.
            if (_removeIcon == null)
            {
                var icons = Resources.FindObjectsOfTypeAll<Sprite>();
                _removeIcon = icons.FirstOrDefault(s => s.name == "Bin")
                    ?? icons.FirstOrDefault(s => s.name == "T_Icons_Tileset_2_Remove_plant");
            }
            wheel.AddAction(_removeIcon!, Labels.Get("farm.removeTarget"), false,
                (Il2CppSystem.Action)(() => Plugin.Guard("Clear farm screen target",
                    () => Save(screen, identity, null))), screen.SelectedId != null);
            wheel.AddCloseAction();
            _screen = screen;
            _identity = identity;
            _wheel = wheel;
            wheel.Open();
        }
        catch
        {
            Close();
            wheel.Clear();
            throw;
        }
    }

    private static void Save(FarmScreen screen, string identity, ItemType? selected, bool automatic = false)
    {
        if (_screen != screen || _identity != identity || !screen.CanUse() ||
            FarmScreen.TargetKey(screen.Module) != identity) return;
        if (selected != null && !screen.Choices().Any(p => p.Guid == selected.Guid)) return;
        // Prepare first so a failed texture creation cannot persist a broken label.
        var shown = automatic ? FarmScreenApi.GetPlantedProduce(screen.Module) : selected;
        FarmScreenPicture.Get(shown?.Icon, screen.Aspect);
        FarmScreens.Targets.Set(identity, automatic ? FarmTargets.Auto : selected?.Guid);
        screen.Refresh();
        // Native click handling closes the wheel after this callback.
    }

    internal static void Tick()
    {
        if (_wheel == null) return;
        if (!_wheel.IsOpen) { Closed(_wheel); return; }
        if (_screen == null || !_screen.gameObject.activeInHierarchy ||
            _screen.Module == null || _screen.Module.MyGhost?.ModuleType == null ||
            FarmScreen.TargetKey(_screen.Module) != _identity)
            Close();
    }

    internal static void Close()
    {
        var wheel = _wheel;
        _wheel = null; _screen = null; _identity = null;
        if (wheel != null && wheel.IsOpen) wheel.Close();
    }

    internal static void Closed(RadialMenuUI wheel)
    {
        if (_wheel == null || _wheel.Pointer != wheel.Pointer) return;
        _wheel = null; _screen = null; _identity = null;
    }
}

[HarmonyPatch(typeof(RadialMenuUI), nameof(RadialMenuUI.Close))]
internal static class FarmProduceWheelClosedPatch
{
    [HarmonyPostfix]
    private static void Postfix(RadialMenuUI __instance) => FarmProduceWheel.Closed(__instance);
}
