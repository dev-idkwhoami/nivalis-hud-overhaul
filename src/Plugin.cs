using BepInEx;
using BepInEx.Logging;
using NivalisMods.ModCompanion.Api;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Nivalis.UI;
using Nivalis.Locale.UI;
using Nivalis.GhostSystem.CustomerLoop;

namespace NivalisMods.HudOverhaul;

[BepInPlugin(Id, "HUD Overhaul", Version)]
[BepInDependency(SettingsRegistry.PluginId, ">=1.0.4")]
public sealed partial class Plugin : BasePlugin
{
    public const string Version = "1.1.9";
    public const string Id = "local.nivalis.hudoverhaul";
    internal static ManualLogSource Logger = null!;

    public override void Load()
    {
        Logger = Log;
        ModStorage.Initialize(Paths.ConfigPath);
        Labels.Load(ModStorage.FilePath(Labels.FileName), message => Log.LogWarning(message));
        CompanionSettings.Register();
        ClassInjector.RegisterTypeInIl2Cpp<CombinedIngredients>();
        ClassInjector.RegisterTypeInIl2Cpp<HistoryPump>();
        ClassInjector.RegisterTypeInIl2Cpp<FarmScreenEditor>();
        ClassInjector.RegisterTypeInIl2Cpp<FarmScreens>();
        ClassInjector.RegisterTypeInIl2Cpp<FarmScreenHost>();
        ClassInjector.RegisterTypeInIl2Cpp<FarmScreen>();
        ClassInjector.RegisterTypeInIl2Cpp<MealSales>();
        ClassInjector.RegisterTypeInIl2Cpp<MealSalesRow>();
        ClassInjector.RegisterTypeInIl2Cpp<QuickActionsPreview>();
        ClassInjector.RegisterTypeInIl2Cpp<QuickAssignButton>();
        ClassInjector.RegisterTypeInIl2Cpp<StaffOrdering>();
        ClassInjector.RegisterTypeInIl2Cpp<StaffOrderButtons>();
        ClassInjector.RegisterTypeInIl2Cpp<SearchDebounce>();
        ClassInjector.RegisterTypeInIl2Cpp<ListSorter>();
        ClassInjector.RegisterTypeInIl2Cpp<ReviewMetrics>();
        ClassInjector.RegisterTypeInIl2Cpp<ShoppingVenues>();
        var harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        ExpandedSearch.Install();
        QuickActionsPreview.Initialize();
        QuickActionBindings.Initialize();
        FarmScreens.Initialize();
        Payroll.Initialize();
        Guard("Initialize history", GameHistory.Initialize);
        AddComponent<HistoryPump>();
        AddComponent<FarmScreenEditor>();
        AddComponent<FarmScreens>();
        // Own the input listener through BepInEx, independently of scene UI startup.
        AddComponent<QuickActionsPreview>();
        Log.LogInfo($"HUD Overhaul {Version} loaded. Settings: {CompanionSettings.ConfigPath}. Data: {ModStorage.Root}. Verbose logging: {IsVerbose}.");
    }

    internal static bool IsVerbose => ModOptions.VerboseLogging?.Value == true;
    internal static void Verbose(string message) { if (IsVerbose) Logger.LogInfo(message); }

    internal static void Guard(string operation, Action action)
    {
        try { action(); }
        catch (Exception e) { Logger.LogError($"{operation}: {e}"); }
    }
}

[HarmonyPatch(typeof(InventoryItemFilteringUi), nameof(InventoryItemFilteringUi.CreateOptions))]
internal static class FilterOptionsPatch
{
    [HarmonyPrefix]
    private static void Prefix(InventoryItemFilteringUi __instance) => Plugin.Guard("Prepare sort options", () =>
        FilterExtension.Prepare(__instance));

}

[HarmonyPatch(typeof(JournalUI), nameof(JournalUI.Refresh))]
internal static class JournalPatch
{
    [HarmonyPostfix]
    private static void Postfix(JournalUI __instance) => Plugin.Guard("Journal sorting", () =>
    {
        ListSorter.Get(__instance.entriesListUi).ApplyDataOrder = mode => DataListSorting.Journal(__instance, mode);
        SortDropdown.Attach(__instance.entriesListUi, __instance.entriesScrollRect,
            new[] {
                new SortMode(Labels.Get("journal.relevant"), SortKey.Native),
                new SortMode(Labels.Get("journal.updated.desc"), SortKey.Updated, true),
                new SortMode(Labels.Get("journal.updated.asc"), SortKey.Updated),
                new SortMode(Labels.Get("journal.active"), SortKey.Active, true)
            }, __instance.Refresh);
    });
}

[HarmonyPatch(typeof(LocaleReviewOverviewTab), nameof(LocaleReviewOverviewTab.RefreshReviewList))]
internal static class ReviewsPatch
{
    [HarmonyPostfix]
    private static void Postfix(LocaleReviewOverviewTab __instance)
    {
        Plugin.Guard("Review sorting", () =>
        {
            ListSorter.Get(__instance.reviewList).ApplyDataOrder = mode => DataListSorting.Reviews(__instance, mode);
            SortDropdown.Attach(__instance.reviewList, __instance.scrollRect, ReviewModes.All, __instance.RefreshReviewList);
        });
    }
}

[HarmonyPatch(typeof(ReviewItemDisplayUi), nameof(ReviewItemDisplayUi.Show))]
internal static class ReviewDataPatch
{
    [HarmonyPostfix]
    private static void Postfix(ReviewItemDisplayUi __instance, Review __0) => Plugin.Guard("Read review sorting data", () =>
    {
        var data = __instance.GetComponent<ReviewMetrics>() ?? __instance.gameObject.AddComponent<ReviewMetrics>();
        data.Time = __0.ReviewTime.TotalGameSeconds;
        data.Score = __0.Score;
        data.Positive = __0.Positive;
        var date = __instance.dateText;
        if (date != null && ModOptions.ReviewTime.Value)
        {
            var time = __0.ReviewTime;
            if (date.text != data.LastDateDisplay) data.BaseDateText = date.text;
            data.LastDateDisplay = Labels.ReviewDate($"{time.ClockHour:00}:{time.ClockMinute:00}", data.BaseDateText ?? "");
            date.text = data.LastDateDisplay;
            date.enableWordWrapping = false;
            if (!data.DateLayoutPrepared)
            {
                // The native date holder is narrow. Expand it and its right-
                // aligned header container so the time fits beside the day.
                var holder = date.transform.parent.GetComponent<UnityEngine.RectTransform>();
                var right = holder.parent.GetComponent<UnityEngine.RectTransform>();
                var layout = holder.GetComponent<UnityEngine.UI.LayoutElement>()
                    ?? holder.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                layout.minWidth = holder.rect.width + 80;
                layout.preferredWidth = layout.minWidth;
                right.sizeDelta = new UnityEngine.Vector2(right.sizeDelta.x + 80, right.sizeDelta.y);
                data.DateLayoutPrepared = true;
            }
        }
    });
}

[HarmonyPatch(typeof(ItemListUI), nameof(ItemListUI.EndUpdate))]
internal static class ListUpdatedPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemListUI __instance) => ListSorter.MarkDirty(__instance);
}

// Batched lists can add entries across multiple frames rather than ending one
// synchronous update. Mark only; sort once after the frame's bindings finish.
[HarmonyPatch(typeof(ItemListUI), nameof(ItemListUI.AddItem))]
internal static class ListAddedPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemListUI __instance) => ListSorter.MarkDirty(__instance);
}
