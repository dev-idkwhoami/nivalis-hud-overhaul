using HarmonyLib;
using Nivalis.Fishing;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Quick use runs during gameplay Update, unlike inventory use while paused.
// Fishing updates can therefore read defaultLocalPose before the rod's Start
// caches it, move the rod to zero, and cause Start to cache that wrong pose.
internal static class QuickEquipmentPose
{
    internal static bool UsingItem;
    private static readonly Dictionary<int, Pose> Pending = new();

    internal static void Prepare(FishingEquipment equipment)
    {
        if (!UsingItem || equipment.rod == null) return;
        var rod = equipment.rod;
        var transform = rod.transform;
        var pose = new Pose(transform.localPosition, transform.localRotation);
        rod.defaultLocalPose = pose;
        Pending[rod.GetInstanceID()] = pose;
        Plugin.Verbose($"Quick fishing equipment: initialized resting pose at {pose.position} before first update.");
    }

    internal static void Started(PlayerFishingRod rod)
    {
        // Keep the original resting pose even if an animation update occurred
        // before native Start. No scale, world placement or fishing state changes.
        if (Pending.Remove(rod.GetInstanceID(), out var pose)) rod.defaultLocalPose = pose;
    }

    internal static void Disabled(PlayerFishingRod rod) => Pending.Remove(rod.GetInstanceID());
}

[HarmonyPatch(typeof(FishingEquipment), nameof(FishingEquipment.Awake))]
internal static class QuickFishingPosePatch
{
    [HarmonyPostfix]
    private static void Postfix(FishingEquipment __instance) => Plugin.Guard("Initialize quick fishing pose", () => QuickEquipmentPose.Prepare(__instance));
}

[HarmonyPatch(typeof(PlayerFishingRod))]
internal static class QuickRodPosePatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(PlayerFishingRod.Start))]
    private static void Started(PlayerFishingRod __instance) => Plugin.Guard("Preserve quick fishing pose", () => QuickEquipmentPose.Started(__instance));

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerFishingRod.OnDisable))]
    private static void Disabled(PlayerFishingRod __instance) => QuickEquipmentPose.Disabled(__instance);
}
