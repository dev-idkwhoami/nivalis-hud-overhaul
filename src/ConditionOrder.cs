using HarmonyLib;
using Nivalis.UI.Greenhouse;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisMods.HudOverhaul;

// Same console layout and navigation fix as the standalone Condition Order mod.
[HarmonyPatch(typeof(GreenhouseConsoleUI))]
internal static class ConditionOrder
{
    private static bool _reportedUnexpectedLayout;

    [HarmonyPostfix, HarmonyPatch(nameof(GreenhouseConsoleUI.Open))]
    private static void AfterOpen(GreenhouseConsoleUI __instance) => ApplyOrder(__instance);

    // The game rebuilds explicit navigation when the input device changes.
    [HarmonyPostfix, HarmonyPatch(nameof(GreenhouseConsoleUI.OnControllerConnected))]
    private static void AfterControllerConnected(GreenhouseConsoleUI __instance) => ApplyOrder(__instance);

    private static void ApplyOrder(GreenhouseConsoleUI ui)
    {
        try
        {
            var humidity = ui.humiditySlider;
            var temperature = ui.temperatureSlider;
            var light = ui.lightnessSlider;
            if (humidity == null || temperature == null || light == null)
                return; // The device-change callback can occur before initialization.

            // Each component is on its complete wrapper: labels, values and the
            // interactive slider move together. Serialized field references stay intact.
            var rows = ModOptions.Conditions.Value
                ? new[] { humidity.transform, temperature.transform, light.transform }
                : new[] { humidity.transform, light.transform, temperature.transform };
            var parent = rows[0].parent;
            if (parent == null || rows[1].parent != parent || rows[2].parent != parent ||
                parent.childCount != 3 || parent.GetComponent<HorizontalLayoutGroup>() == null)
            {
                if (!_reportedUnexpectedLayout)
                {
                    _reportedUnexpectedLayout = true;
                    Plugin.Logger.LogWarning("Condition rows no longer match the expected three-column layout; leaving them unchanged.");
                }
                return;
            }

            var sliders = ModOptions.Conditions.Value
                ? new[] { humidity.slider, temperature.slider, light.slider }
                : new[] { humidity.slider, light.slider, temperature.slider };
            // Preserve external navigation at the two ends. Auto navigation uses
            // the new screen positions naturally; only explicit links need changing.
            var first = Array.FindIndex(rows, row => row.GetSiblingIndex() == 0);
            var last = Array.FindIndex(rows, row => row.GetSiblingIndex() == 2);
            var explicitNavigation = Array.TrueForAll(sliders,
                slider => slider != null && slider.navigation.mode == Navigation.Mode.Explicit);
            Selectable? left = null;
            Selectable? right = null;
            if (explicitNavigation)
            {
                left = sliders[first].navigation.selectOnLeft;
                right = sliders[last].navigation.selectOnRight;
                // Internal end links indicate wraparound rather than an external control.
                if (Array.Exists(sliders, slider => slider == left)) left = sliders[2];
                if (Array.Exists(sliders, slider => slider == right)) right = sliders[0];
            }

            var changed = rows[1].GetSiblingIndex() != 1 || rows[0].GetSiblingIndex() != 0;
            for (var i = 0; i < rows.Length; i++)
                rows[i].SetSiblingIndex(i);

            if (explicitNavigation)
            {
                for (var i = 0; i < sliders.Length; i++)
                {
                    var navigation = sliders[i].navigation;
                    navigation.selectOnLeft = i == 0 ? left : sliders[i - 1];
                    navigation.selectOnRight = i == 2 ? right : sliders[i + 1];
                    sliders[i].navigation = navigation;
                }
            }

            if (changed)
            {
                LayoutRebuilder.MarkLayoutForRebuild(parent.Cast<RectTransform>());
                Plugin.Verbose("Greenhouse console reordered: Humidity -> Temperature -> Light.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogError($"Could not reorder greenhouse condition UI: {exception}");
        }
    }
}
