using BepInEx.Configuration;
using Nivalis;
using Nivalis.UI;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class ModOptions
{
    internal static ConfigEntry<bool> Search = null!, Sorting = null!, QuickActions = null!, Staff = null!, Quests = null!,
        Scan = null!, Conditions = null!, ReviewTime = null!, FastReviews = null!, Sales = null!, Ingredients = null!, Screens = null!;
    internal static ConfigEntry<bool> VerboseLogging = null!;
    internal static ConfigEntry<bool> DeductVenueStock = null!;
    internal static ConfigEntry<int> StockDays = null!, DebounceMs = null!;
    internal static void Initialize(ConfigFile config)
    {
        VerboseLogging = config.Bind("Logging", "Verbose", false,
            "Enable detailed HUD Overhaul diagnostics in BepInEx/LogOutput.log. Warnings and errors are always logged. This does not disable saved gameplay history. Restart after editing this file, or use the in-game checkbox.");
        DeductVenueStock = config.Bind("ShoppingList", "DeductVenueStock", true,
            "Deduct existing venue ingredient stock in both Standard and Estimated shopping lists. Carried inventory is counted separately.");
        DeductVenueStock.SettingChanged += (_, _) => Plugin.Guard("Refresh shopping stock mode", EstimatedShopping.RefreshPanels);
        ConfigEntry<bool> Feature(string name) => config.Bind("Features", name, true, "Enable " + name + ". Changes apply without restarting.");
        Search = Feature("ExpandedSearch"); Sorting = Feature("AdditionalSorting"); QuickActions = Feature("QuickActions");
        Staff = Feature("StaffOrdering"); Quests = Feature("PinnedQuestFiltering"); Scan = Feature("DistinctScanColors");
        Conditions = Feature("ConditionOrder"); ReviewTime = Feature("ReviewTimestamps"); FastReviews = Feature("VirtualReviews");
        Sales = Feature("SalesStatistics"); Ingredients = Feature("CombinedIngredients"); Screens = Feature("FarmScreens");
        StockDays = config.Bind("ShoppingList", "StockTargetDays", 1,
            new ConfigDescription("Days of estimated ingredient demand to stock.", new AcceptableValueRange<int>(1, 14)));
        DebounceMs = config.Bind("Search", "DebounceMilliseconds", 250,
            new ConfigDescription("Delay before refreshing search results; zero disables the delay.", new AcceptableValueRange<int>(0, 1000)));
        Quests.SettingChanged += (_, _) => Plugin.Guard("Refresh quest setting", () =>
        {
            if (!QuestPatchRegistration.Installed) return;
            QuestTracking.Invalidate();
            foreach (var hud in UnityEngine.Object.FindObjectsOfType<ActiveJournalEntriesUi>()) hud.Refresh();
        });
        Scan.SettingChanged += (_, _) => { if (!Scan.Value) ScanMarkerColors.RestoreAll(); };
        Sorting.SettingChanged += (_, _) => Plugin.Guard("Refresh sorting setting", FilterExtension.RefreshSettings);
        Screens.SettingChanged += (_, _) => Plugin.Guard("Refresh farm screens setting", () =>
        {
            FarmProduceWheel.Close(); FarmScreenEditor.ExitMode();
            foreach (var screen in Resources.FindObjectsOfTypeAll<FarmScreen>())
                if (screen != null && screen.gameObject.scene.IsValid()) screen.gameObject.SetActive(Screens.Value);
            FarmScreens.RequestScan();
        });
    }
}
