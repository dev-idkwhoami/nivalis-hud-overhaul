using HarmonyLib;
using Nivalis;
using Nivalis.UI;
using Nivalis.Fishing;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Opt-in comparison trace: observes normal inventory and quick-slot equips
// without changing transforms, prefab data, or equipment initialization.
internal static class EquipmentTrace
{
    internal static string Source = "native";
    private static FishingEquipment? _equipment;
    private static string _source = "native";
    private static int _frame;
    private static int _sample;
    private static readonly int[] SampleFrames = { 1, 5, 30 };

    internal static void BeforeUse(InventoryItemDisplayUI row)
    {
        if (!Plugin.IsVerbose) return;
        var type = row._stack?.Type;
        var prefab = type?.entityPrefab;
        if (prefab == null || prefab.GetComponentInChildren<FishingEquipment>(true) == null) return;
        Plugin.Verbose($"Equipment trace {Source}: Use {type!.Name}, id={type.Guid}, frame={Time.frameCount}, timeScale={Time.timeScale}, prefab={Describe(prefab.transform)}");
        var holder = PlayerManager.Instance?.LocalPlayer?.Character?.ObjectHolder;
        if (holder != null) Plugin.Verbose($"Equipment trace {Source}: holder={Describe(holder.Container)}, previous={holder.HeldObject?.name}");
    }

    internal static void Created(FishingEquipment equipment)
    {
        if (!Plugin.IsVerbose) return;
        _equipment = equipment;
        _source = Source;
        _frame = Time.frameCount;
        _sample = 0;
        Snapshot(equipment, "Awake");
    }

    internal static void Snapshot(FishingEquipment equipment, string stage)
    {
        if (!Plugin.IsVerbose || _equipment == null || equipment.Pointer != _equipment.Pointer) return;
        Plugin.Verbose($"Equipment trace {_source} {stage} frame={Time.frameCount}: root={Describe(equipment.transform)}; rod={Describe(equipment.rod?.transform)}; handle={Describe(equipment.rod?.Handle)}; detector={Describe(equipment.detector?.transform)}");
        var camera = Camera.main;
        if (camera != null)
            Plugin.Verbose($"Equipment trace {_source} {stage}: camera={camera.name}, fov={camera.fieldOfView}, cameraTransform={Describe(camera.transform)}");
        if (equipment.rod == null) return;
        var meshes = equipment.rod.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < Math.Min(meshes.Length, 4); i++)
            Plugin.Verbose($"Equipment trace {_source} {stage}: mesh={Describe(meshes[i].transform)}, bounds={meshes[i].bounds.size}");
    }

    internal static void Tick()
    {
        if (!Plugin.IsVerbose || _equipment == null || _sample >= SampleFrames.Length || Time.frameCount - _frame < SampleFrames[_sample]) return;
        var sample = SampleFrames[_sample++];
        Snapshot(_equipment, $"after {sample} frames");
    }

    private static string Describe(Transform? t) => t == null ? "none" :
        $"{t.name} parent={t.parent?.name} localScale={t.localScale} worldScale={t.lossyScale} localPos={t.localPosition} worldPos={t.position}";
}

[HarmonyPatch(typeof(InventoryItemDisplayUI), nameof(InventoryItemDisplayUI.HoldButtonListener))]
internal static class EquipmentUseTracePatch
{
    [HarmonyPrefix]
    private static void Prefix(InventoryItemDisplayUI __instance, out string __state)
    {
        __state = EquipmentTrace.Source;
        EquipmentTrace.Source = __instance.name == "HUDOverhaul.InventoryActionAdapter" ? "quick-slot" : "inventory";
        Plugin.Guard("Trace equipment use", () => EquipmentTrace.BeforeUse(__instance));
    }

    [HarmonyFinalizer]
    private static void Finalizer(string __state) => EquipmentTrace.Source = __state;
}

[HarmonyPatch(typeof(FishingEquipment))]
internal static class FishingEquipmentTracePatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(FishingEquipment.Awake))]
    private static void Awake(FishingEquipment __instance) => Plugin.Guard("Trace fishing Awake", () => EquipmentTrace.Created(__instance));

    [HarmonyPostfix, HarmonyPatch(nameof(FishingEquipment.Start))]
    private static void Start(FishingEquipment __instance) => Plugin.Guard("Trace fishing Start", () => EquipmentTrace.Snapshot(__instance, "Start"));
}

[HarmonyPatch(typeof(PlayerObjectHolder), nameof(PlayerObjectHolder.HoldObject))]
internal static class HeldEquipmentTracePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerObjectHolder __instance) => Plugin.Guard("Trace held equipment", () =>
    {
        if (!Plugin.IsVerbose) return;
        var equipment = __instance.HeldObject?.GetComponentInChildren<FishingEquipment>(true);
        if (equipment != null) EquipmentTrace.Snapshot(equipment, "HoldObject");
    });
}

[HarmonyPatch(typeof(Nivalis.Locale.UI.InventoryItemDetailsDisplay), nameof(Nivalis.Locale.UI.InventoryItemDetailsDisplay.OnHoldButtonClicked))]
internal static class EquipmentDetailsTracePatch
{
    [HarmonyPrefix]
    private static void Prefix(out string __state)
    {
        __state = EquipmentTrace.Source;
        EquipmentTrace.Source = "inventory-details";
    }

    [HarmonyFinalizer]
    private static void Finalizer(string __state) => EquipmentTrace.Source = __state;
}
