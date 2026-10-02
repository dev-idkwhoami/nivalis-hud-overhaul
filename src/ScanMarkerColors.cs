using HarmonyLib;
using Nivalis.Scanning;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

[HarmonyPatch(typeof(ScannablePoolElement), nameof(ScannablePoolElement.Ping))]
internal static class ScanMarkerColors
{
    private static readonly Dictionary<int, (ScannablePoolElement Element, Color Color)> Originals = new();
    [HarmonyPrefix]
    private static void BeforePing(ScannablePoolElement __instance)
    {
        if (Originals.Remove(__instance.GetInstanceID(), out var original)) Restore(original.Element, original.Color);
    }
    internal static void RestoreAll()
    {
        foreach (var original in Originals.Values) Restore(original.Element, original.Color);
        Originals.Clear();
    }
    private static void Restore(ScannablePoolElement element, Color color)
    {
        if (element == null) return;
        element.defaultColor = color;
        if (element.spriteRenderer != null)
        {
            color.a = element.spriteRenderer.color.a;
            element.spriteRenderer.color = color;
        }
    }
    private static bool _loggedApartment;
    private static bool _loggedShelter;

    [HarmonyPostfix]
    private static void Postfix(ScannablePoolElement __instance, Scannable __0) =>
        Plugin.Guard("Apartment scan marker color", () => Apply(__instance, __0));

    private static void Apply(ScannablePoolElement element, Scannable origin)
    {
        if (!ModOptions.Scan.Value || origin == null || element.spriteRenderer == null) return;
        var marker = origin.GetComponentInParent<ApartmentKeyMarkerController>();
        if (marker == null || marker.apartment == null || marker.normalMarker == null) return;
        // Only the ordinary property marker: keep the separate curfew warning
        // and unrelated scannables inside an apartment in their native style.
        if (!origin.transform.IsChildOf(marker.normalMarker.transform)) return;

        var shelter = marker.apartment.Shelter;
        var tint = shelter ? new Color(0.25f, 0.9f, 1f) : new Color(1f, 0.73f, 0.28f);
        var baseline = element.defaultColor;
        Originals[element.GetInstanceID()] = (element, baseline);
        tint.a = baseline.a;
        // Native Update reads defaultColor.rgb and computes its own alpha.
        // Update both the stored baseline and this frame's rendered color.
        element.defaultColor = tint;
        tint.a = element.spriteRenderer.color.a;
        element.spriteRenderer.color = tint;

        if (shelter ? _loggedShelter : _loggedApartment) return;
        if (shelter) _loggedShelter = true;
        else _loggedApartment = true;
        Plugin.Verbose($"Scan marker color applied: {(shelter ? "public shelter (cyan)" : "apartment (gold)")}.");
    }
}
