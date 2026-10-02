using HarmonyLib;
using Nivalis;
using Nivalis.UI.Concrete;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace NivalisMods.HudOverhaul;

// Keep native input display, rebinding, overlay and save handling together.
[HarmonyPatch(typeof(ControlsSettingsUI), nameof(ControlsSettingsUI.Start))]
internal static class NativeModControls
{
    private const string Prefix = "HUDOverhaul.Binding.";

    [HarmonyPrefix]
    private static void BeforeStart(ControlsSettingsUI __instance) => Plugin.Guard("Add mod controls", () =>
    {
        var source = __instance.GetComponentsInChildren<InputRebindUI>(true).FirstOrDefault(row => row.name == "Inventory");
        if (source == null || source.transform.parent.Find(Prefix + "quickActions") != null) return;
        var input = Nivalis.PlayerInputManager._instance;
        if (input?.Input == null) return;
        QuickActionBindings.Register(input.Input);
        var staging = new GameObject("HUDOverhaul.ControlsStaging");
        staging.SetActive(false);
        try
        {
            foreach (var (actionName, label) in new[] {
                (QuickActionBindings.ActionName, "quickActions"),
                (QuickActionBindings.MenuName, "menu"),
                (QuickActionBindings.EditName, "editor") })
            {
                var clone = Object.Instantiate(source.gameObject, staging.transform);
                clone.SetActive(false);
                clone.name = Prefix + label;
                var row = clone.GetComponent<InputRebindUI>();
                row.action = new InputDisplayUI.ActionDisplay
                {
                    actionReference = InputActionReference.Create(QuickActionBindings.Find(actionName)),
                    compositeIndex = new[] { 0 },
                    description = source.action.description,
                    displayInputText = source.action.displayInputText
                };
                row.action.Init();
                // Native Start wires selection and rebind-overlay callbacks for
                // every entry in this list, including the appended mod rows.
                __instance.controls.Add(row);
                clone.transform.SetParent(source.transform.parent, false);
                clone.transform.SetAsLastSibling();
                clone.SetActive(true);
                Relabel(row);
            }
        }
        finally { Object.Destroy(staging); }
    });

    internal static void Relabel(InputRebindUI row)
    {
        if (row != null && row.name.StartsWith(Prefix, StringComparison.Ordinal))
            row.actionText.text = Labels.Get("settings.key." + row.name.Substring(Prefix.Length));
    }
}

[HarmonyPatch(typeof(InputRebindUI), nameof(InputRebindUI.UpdateText))]
internal static class NativeModControlLabel
{
    [HarmonyPostfix]
    private static void Postfix(InputRebindUI __instance) => NativeModControls.Relabel(__instance);
}

[HarmonyPatch(typeof(InputRebindUI), nameof(InputRebindUI.Awake))]
internal static class NativeModControlAwakeLabel
{
    [HarmonyPostfix]
    private static void Postfix(InputRebindUI __instance) => NativeModControls.Relabel(__instance);
}
