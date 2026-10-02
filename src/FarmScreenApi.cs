using Nivalis;
using Nivalis.InventorySystem;

namespace NivalisMods.HudOverhaul;

public enum FarmScreenMode { None, Manual, Auto }

/// <summary>Read-only integration API. Call on Unity's main thread with a loaded module.</summary>
public static class FarmScreenApi
{
    public static FarmScreenMode GetMode(GreenhouseModuleView module)
    {
        var value = Selection(module);
        return value == null ? FarmScreenMode.None : value == FarmTargets.Auto ? FarmScreenMode.Auto : FarmScreenMode.Manual;
    }

    /// <summary>Explicit manual target; null for Auto, no label, or an unavailable module.</summary>
    public static ItemType? GetSelectedProduce(GreenhouseModuleView module)
    {
        var value = Selection(module);
        if (value == null || value == FarmTargets.Auto) return null;
        return module.MyGhost.ModuleType.Plants?.ToArray().FirstOrDefault(p => p != null && p.Guid == value);
    }

    /// <summary>Effective label; Auto resolves live planted produce and returns null on empty modules.</summary>
    public static ItemType? GetDisplayedProduce(GreenhouseModuleView module) =>
        GetMode(module) == FarmScreenMode.Auto ? GetPlantedProduce(module) : GetSelectedProduce(module);

    internal static ItemType? GetPlantedProduce(GreenhouseModuleView module) =>
        !FarmScreens.Loading && module != null && module.MyGhost?.Planted == true ? module.MyGhost.PlantType : null;

    private static string? Selection(GreenhouseModuleView module) =>
        FarmScreens.Loading || module == null || module.MyGhost?.ModuleType == null
            ? null : FarmScreens.Targets.Get(FarmScreen.TargetKey(module));
}
