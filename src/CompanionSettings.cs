using NivalisMods.ModCompanion.Api;

namespace NivalisMods.HudOverhaul;

internal static class CompanionSettings
{
    internal const string ConfigPath = "HUDOverhaul/HUDOverhaul.cfg";

    internal static void Register()
    {
        var mod = SettingsRegistry.Register(Plugin.Id,
            new ModMetadata("HUD Overhaul", "dev-idkwhoami", Plugin.Version,
                "Search, sorting, shopping, farm screens and other HUD improvements.",
                Icon: ModIcon.FromResource(typeof(Plugin).Assembly, "HudOverhaul.Icon.png")), ConfigPath);
        var features = mod.AddCategory("Features", Labels.Get("settings.category.features"));
        var shopping = mod.AddCategory("Shopping", Labels.Get("settings.category.shopping"));
        var farm = mod.AddCategory("Farm", Labels.Get("settings.category.farm"));
        ModOptions.Initialize(features, shopping, farm, mod.Developer);
        EstimatedShopping.Initialize(shopping);
        FarmScreenEditor.Initialize(farm);
        mod.Controls.AddNativeInput("QuickActions", Labels.Get("settings.key.quickActions"), QuickActionBindings.ActionName);
        mod.Controls.AddNativeInput("FarmEditor", Labels.Get("settings.key.editor"), QuickActionBindings.EditName);
        mod.Info.AddHeading("HUD Overhaul");
        mod.Info.AddParagraph("Configure individual HUD features, shopping estimates and farm screen editing in the tabs above.");
        mod.Info.AddHeading("Farm editor shortcuts");
        mod.Info.AddParagraph(Labels.Get("settings.editorShortcuts.body"));
    }

    internal static void SetQuestAvailability()
    {
        ModOptions.Quests.EnabledWhen = () => QuestPatchRegistration.Installed;
        ModOptions.Quests.DisabledReason = QuestPatchRegistration.UnavailableText;
    }
}
