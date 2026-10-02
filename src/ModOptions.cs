using NivalisMods.ModCompanion.Api;
using Nivalis;
using Nivalis.UI;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class ModOptions
{
    internal static Setting<bool> Search = null!, Sorting = null!, QuickActions = null!, Staff = null!, Quests = null!,
        Scan = null!, Conditions = null!, ReviewTime = null!, FastReviews = null!, Sales = null!, Ingredients = null!, Screens = null!;
    internal static Setting<bool> VerboseLogging = null!;
    internal static Setting<bool> DeductVenueStock = null!;
    internal static Setting<int> StockDays = null!, DebounceMs = null!;
    internal static void Initialize(SettingsCategory features, SettingsCategory shopping, SettingsCategory farm, DeveloperSection developer)
    {
        Setting<bool> Feature(string key, string label) => features.Toggle(key, Labels.Get("settings.option." + label), true);
        Search = Feature("Search", "search"); Sorting = Feature("Sorting", "sorting");
        QuickActions = Feature("QuickActions", "quickActions"); Staff = Feature("StaffOrdering", "staff");
        Quests = Feature("PinnedQuests", "quests"); Scan = Feature("ScanColors", "scan");
        Conditions = Feature("ConditionOrder", "conditions"); ReviewTime = Feature("ReviewTimestamps", "reviewTime");
        FastReviews = Feature("FastReviews", "fastReviews"); Sales = Feature("SalesStatistics", "sales");
        Ingredients = Feature("CombinedIngredients", "ingredients");
        DebounceMs = features.Stepper("SearchDelayMs", Labels.Get("settings.option.debounce"), 250, 0, 1000, 50);
        DebounceMs.Hint = "Delay before refreshing search results; zero disables the delay.";
        DeductVenueStock = shopping.Choice("VenueStock", Labels.Get("settings.option.venueStock"), true,
            new[] { new Choice<bool>(true, Labels.Get("shopping.stock.deduct")), new Choice<bool>(false, Labels.Get("shopping.stock.ignore")) });
        StockDays = shopping.Stepper("StockDays", Labels.Get("settings.option.stockDays"), 1, 1, 14);
        Screens = farm.Toggle("Screens", Labels.Get("settings.option.screens"), true);
        VerboseLogging = developer.AddVerboseLogging(false,
            "Detailed diagnostics in BepInEx/LogOutput.log. Saved gameplay history is independent of logging verbosity.");
        DeductVenueStock.Changed += _ => Plugin.Guard("Refresh shopping stock mode", EstimatedShopping.RefreshPanels);
        Quests.Changed += _ => Plugin.Guard("Refresh quest setting", () =>
        {
            if (!QuestPatchRegistration.Installed) return;
            QuestTracking.Invalidate();
            foreach (var hud in UnityEngine.Object.FindObjectsOfType<ActiveJournalEntriesUi>()) hud.Refresh();
        });
        Scan.Changed += _ => { if (!Scan.Value) ScanMarkerColors.RestoreAll(); };
        Sorting.Changed += _ => Plugin.Guard("Refresh sorting setting", FilterExtension.RefreshSettings);
        Screens.Changed += _ => Plugin.Guard("Refresh farm screens setting", () =>
        {
            FarmProduceWheel.Close(); FarmScreenEditor.ExitMode();
            foreach (var screen in Resources.FindObjectsOfTypeAll<FarmScreen>())
                if (screen != null && screen.gameObject.scene.IsValid()) screen.gameObject.SetActive(Screens.Value);
            FarmScreens.RequestScan();
        });
    }
}
