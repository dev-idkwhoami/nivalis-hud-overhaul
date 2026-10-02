using HarmonyLib;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;
using Nivalis.Playables;
using Nivalis.UI;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Placement/Inspect preview. Copy only TV transforms/meshes: no furniture, TV,
// ghost, audio or light scripts are instantiated or saved. A separate native
// Interactable belongs only to the screen.
[HarmonyPatch(typeof(GreenhouseModuleView), nameof(GreenhouseModuleView.Inspect))]
internal static class FarmLabelPreview
{
    private const string LabelName = "HUDOverhaul.FarmLabelPreview";

    [HarmonyPostfix]
    private static void Postfix(GreenhouseModuleView __instance) =>
        Plugin.Guard("Farm screen preview", () => Attach(__instance));

    internal static void Attach(GreenhouseModuleView module)
    {
        if (!ModOptions.Screens.Value || module == null || !FarmScreenEditor.HasSaved(FarmScreenEditor.ModelKey(module))) return;
        var previewName = LabelName + "." + FarmScreenEditor.ScreenId;
        var existing = module.transform.Find(previewName);
        if (existing != null)
        {
            existing.GetComponent<FarmScreen>()?.Refresh();
            return;
        }
        // Replacing the test TV must not include the old preview in module bounds.
        for (var i = module.transform.childCount - 1; i >= 0; i--)
        {
            var child = module.transform.GetChild(i);
            if (!child.name.StartsWith(LabelName, StringComparison.Ordinal)) continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        var prefab = ItemDatabase.Instance.TryProvide(FarmScreenEditor.ScreenId)?.TryCast<ItemType>()?.EntityPrefab;
        if (prefab == null) throw new InvalidOperationException("Flat TV prefab unavailable.");
        var billboard = prefab.GetComponentInChildren<AnimatedBillboard>(true);
        var display = billboard?.meshRenderer ?? billboard?.GetComponent<MeshRenderer>();
        if (display == null || display.GetComponent<MeshFilter>()?.sharedMesh == null)
            throw new InvalidOperationException("Flat TV screen mesh unavailable.");

        var layer = module.gameObject.layer;
        foreach (var renderer in module.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.TryCast<ParticleSystemRenderer>() != null) continue;
            layer = renderer.gameObject.layer;
            break;
        }
        var root = new GameObject(previewName);
        root.SetActive(false);
        try
        {
            MeshRenderer? screen = null;
            var visuals = new GameObject("TV visuals").transform;
            visuals.SetParent(root.transform, false);
            CopyVisuals(prefab.transform, visuals, display, layer, ref screen, true);
            if (screen == null) throw new InvalidOperationException("Screen was not copied.");
            var mesh = screen.GetComponent<MeshFilter>().sharedMesh;
            var face = mesh.bounds;
            AlignToCalibration(visuals, screen);
            var displayScale = screen.transform.lossyScale;
            var aspect = face.size.x * Mathf.Abs(displayScale.x) / (face.size.y * Mathf.Abs(displayScale.y));
            screen.sharedMaterial = FarmScreenPicture.Get(null, aspect);

            // Every attached model has a calibrated local pose; applying it
            // directly avoids measuring/reorienting every machine at load time.
            root.transform.SetParent(module.transform, false);
            FarmScreenEditor.ApplySaved(root.transform, FarmScreenEditor.ModelKey(module));
            // No physics components: placement and furniture overlap checks
            // must never count a cosmetic display as an obstacle.
            root.AddComponent<Interactable>().IsActive = true;
            root.AddComponent<FarmScreen>().Bind(module, screen, aspect);
            root.SetActive(true);
        }
        catch { UnityEngine.Object.Destroy(root); throw; }
    }

    private static void AlignToCalibration(Transform visuals, MeshRenderer screen)
    {
        var reference = ItemDatabase.Instance.TryProvide(FarmScreenEditor.DefaultScreen)?.TryCast<ItemType>()?.EntityPrefab;
        var referenceBillboard = reference?.GetComponentInChildren<AnimatedBillboard>(true);
        var referenceScreen = referenceBillboard?.meshRenderer ?? referenceBillboard?.GetComponent<MeshRenderer>();
        if (reference == null || referenceScreen == null) throw new InvalidOperationException("Calibration reference screen unavailable.");
        var face = referenceScreen.GetComponent<MeshFilter>().sharedMesh.bounds;
        var oldRoot = reference.transform;
        var center = oldRoot.InverseTransformPoint(referenceScreen.transform.TransformPoint(face.center));
        var normal = oldRoot.InverseTransformDirection(referenceScreen.transform.TransformDirection(Vector3.back));
        var up = oldRoot.InverseTransformDirection(referenceScreen.transform.TransformDirection(Vector3.up));
        var width = oldRoot.InverseTransformVector(referenceScreen.transform.TransformVector(Vector3.right * face.size.x)).magnitude;
        var newFace = screen.GetComponent<MeshFilter>().sharedMesh.bounds;
        var newWidth = screen.transform.TransformVector(Vector3.right * newFace.size.x).magnitude;
        if (width <= 0 || newWidth <= 0) throw new InvalidOperationException("Invalid TV calibration width.");
        visuals.rotation = Quaternion.LookRotation(normal, up) * Quaternion.Inverse(
            Quaternion.LookRotation(screen.transform.TransformDirection(Vector3.back), screen.transform.TransformDirection(Vector3.up)));
        visuals.localScale = Vector3.one * (width / newWidth);
        visuals.position += center - screen.transform.TransformPoint(newFace.center);
    }

    private static void CopyVisuals(Transform source, Transform target, MeshRenderer sourceScreen,
        int layer, ref MeshRenderer? screen, bool isRoot = false)
    {
        target.gameObject.layer = layer;
        if (!isRoot)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }
        var filter = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        if (filter?.sharedMesh != null && renderer != null &&
            (renderer.enabled || renderer.Pointer == sourceScreen.Pointer) &&
            !source.name.EndsWith("_Shadow", StringComparison.OrdinalIgnoreCase))
        {
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = target.gameObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            copy.receiveShadows = renderer.receiveShadows;
            if (renderer.Pointer == sourceScreen.Pointer)
            {
                copy.enabled = true;
                screen = copy;
            }
        }
        for (var i = 0; i < source.childCount; i++)
        {
            var child = source.GetChild(i);
            // TV startup can leave its display (or a parent) inactive. The
            // preview omits that startup code, so explicitly include this path.
            var containsScreen = child.Pointer == sourceScreen.transform.Pointer || sourceScreen.transform.IsChildOf(child);
            if (!child.gameObject.activeSelf && !containsScreen) continue;
            var copy = new GameObject(child.name);
            copy.transform.SetParent(target, false);
            CopyVisuals(child, copy.transform, sourceScreen, layer, ref screen);
        }
    }

}

// Place starts a coroutine; its second step finishes physics setup and invokes
// the game's completion callback. Attach afterward, never to a placement ghost.
[HarmonyPatch(typeof(HoldableEntity._PlaceRoutine_d__94), nameof(HoldableEntity._PlaceRoutine_d__94.MoveNext))]
internal static class FarmScreenPlacementPatch
{
    [HarmonyPrefix]
    private static void Prefix(HoldableEntity._PlaceRoutine_d__94 __instance, out bool __state) =>
        __state = __instance.__1__state == 1;

    [HarmonyPostfix]
    private static void Postfix(HoldableEntity._PlaceRoutine_d__94 __instance, bool __result, bool __state)
    {
        if (!__state || __result) return;
        Plugin.Guard("Farm screen placement", () =>
        {
            var placed = __instance.__4__this;
            if (placed == null || !placed.gameObject.activeInHierarchy) return;
            var module = placed.GetComponent<GreenhouseModuleView>()
                ?? placed.GetComponentInChildren<GreenhouseModuleView>();
            if (module != null) FarmLabelPreview.Attach(module);
        });
    }
}
