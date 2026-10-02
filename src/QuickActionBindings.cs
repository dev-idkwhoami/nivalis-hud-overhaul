using HarmonyLib;
using Nivalis;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInputManager = Nivalis.PlayerInputManager;

namespace NivalisMods.HudOverhaul;

internal static class QuickActionBindings
{
    internal const string ActionName = "HUDOverhaulQuickActions";
    // Inert action identity retained because native save packets may reference it.
    // No settings are imported and no HUD menu listens to this action.
    private const string RetiredMenuAction = "HUDOverhaulMenu";
    internal const string EditName = "HUDOverhaulFarmEditor";
    internal const string MapName = "HUDOverhaul";
    private static bool _rebinding;
    private static float _resumeAfter;

    internal static void Initialize()
    {
        PlayerInputManager.OnRebindStarted += (Il2CppSystem.Action)(() => _rebinding = true);
        PlayerInputManager.OnRebindComplete += (Il2CppSystem.Action)RebindingEnded;
        PlayerInputManager.OnRebindCanceled += (Il2CppSystem.Action)RebindingEnded;
    }

    private static void RebindingEnded()
    {
        _rebinding = false;
        _resumeAfter = Time.unscaledTime + 0.25f;
    }

    internal static void Register(GameControls controls)
    {
        var asset = controls.asset;
        if (asset.FindAction(ActionName, false) != null) return;
        // Called after input-manager initialization, while its maps are still
        // disabled. The saved overrides are reloaded after adding this action.
        // Stable IDs let the game's normal binding save/load own this.
        var map = new InputActionMap(MapName) { m_Id = "045cb0df-b7a7-4dea-aa85-97d63d68373f" };
        var action = InputActionSetupExtensions.AddAction(map, ActionName, InputActionType.Button,
            expectedControlLayout: "Button");
        action.m_Id = "4b449c51-32cb-4347-aa1f-244642d54844";
        InputActionSetupExtensions.AddBinding(action, new InputBinding("<Keyboard>/q", groups: controls.KeyboardScheme.bindingGroup)
        { m_Id = "695ab56e-cdb6-485a-9d8c-17313a180e95" });
        InputActionSetupExtensions.AddBinding(action, new InputBinding("", groups: controls.GamepadScheme.bindingGroup)
        { m_Id = "f42a85ee-bd45-473e-a553-d9b878e5fa86" });
        foreach (var (name, key, id, bindingId) in new[] {
            (RetiredMenuAction, "", "27285b95-ad75-4f7e-b1cc-f32b71c7ee15", "bbef22af-2687-4366-bd18-4a171561409c"),
            (EditName, "f4", "f453631d-101b-4866-a2df-26181961e1d5", "d589a6c5-5ba3-4be7-83d7-08a54d9f3a67") })
        {
            var extra = InputActionSetupExtensions.AddAction(map, name, InputActionType.Button, expectedControlLayout: "Button");
            extra.m_Id = id;
            InputActionSetupExtensions.AddBinding(extra, new InputBinding(key.Length == 0 ? "" : "<Keyboard>/" + key, groups: controls.KeyboardScheme.bindingGroup) { m_Id = bindingId });
            if (name == EditName)
                InputActionSetupExtensions.AddBinding(extra, new InputBinding("", groups: controls.GamepadScheme.bindingGroup)
                { m_Id = "f3a130ad-51ad-4013-86c6-73d4bfeb0f79" });
        }
        InputActionSetupExtensions.AddActionMap(asset, map);
        Plugin.Verbose("Quick Actions registered in native input settings.");
    }

    internal static InputAction? Find(string name) => PlayerInputManager._instance?.Input?.asset.FindAction(name, false);
    internal static string Display(string name)
    {
        var action = Find(name);
        return action == null ? Labels.Get("quickActions.unbound") : InputActionRebindingExtensions.GetBindingDisplayString(action, 0);
    }
    internal static bool Pressed(string name = ActionName)
    {
        var input = PlayerInputManager._instance;
        if (input == null || input.Input == null || _rebinding || input.currentRebind != null || Time.unscaledTime < _resumeAfter)
            return false;
        var action = Find(name);
        if (action == null) return false;
        if (!action.enabled) { action.Enable(); return false; }
        return action.WasPressedThisFrame();
    }

}

[HarmonyPatch(typeof(PlayerInputManager), nameof(PlayerInputManager.InitializeInternal))]
internal static class QuickActionsRegistrationPatch
{
    [HarmonyPrefix]
    private static void Prefix(ISavePacket __1, out string? __state)
    {
        var saved = __1?.TryCast<PlayerInputManagerSave>();
        __state = saved?.BindingJsonString;
        // Native initialization creates GameControls then immediately restores
        // overrides. Custom actions do not exist yet, and this game's Input
        // System throws instead of skipping an unknown saved action.
        if (saved != null) saved.BindingJsonString = null!;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerInputManager __instance, ISavePacket __1, string? __state)
    {
        var saved = __1?.TryCast<PlayerInputManagerSave>();
        if (saved != null) saved.BindingJsonString = __state!;
        Plugin.Guard("Register Quick Actions binding", () =>
        {
            QuickActionBindings.Register(__instance.Input);
            if (!string.IsNullOrEmpty(__state)) __instance.LoadBindings(saved!);
            Plugin.Verbose("Quick Actions input initialized; saved bindings restored after registration.");
        });
    }

    [HarmonyFinalizer]
    private static void Finalizer(ISavePacket __1, string? __state)
    {
        // Restore the original in-memory packet even if initialization fails.
        // No save file is edited and unrelated key overrides remain intact.
        var saved = __1?.TryCast<PlayerInputManagerSave>();
        if (saved != null) saved.BindingJsonString = __state!;
    }
}
