using BepInEx;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.InventorySystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NivalisMods.HudOverhaul;

public sealed class FarmScreens : MonoBehaviour
{
    internal static readonly FarmTargets Targets = new(Path.Combine(ModStorage.Root, "HUDOverhaul.farm-targets"));
    private static float _scanAt = -1;
    private static string? _pendingLoad;
    internal static bool Loading;
    public FarmScreens(IntPtr pointer) : base(pointer) { }
    internal static void Initialize()
    {
        SerializationManager.OnPostLoad.Add((Il2CppSystem.Action)(() => Plugin.Guard("Load farm targets", FinishLoad)));
        SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)((_, _) => RequestScan());
        RequestScan();
    }
    internal static void RequestScan() => _scanAt = Time.unscaledTime + 0.5f;
    internal static void BeginLoad(string slot) { _pendingLoad = slot; Loading = true; }
    internal static void Clear()
    {
        Targets.Clear(); FarmProduceWheel.Close(); FarmScreenEditor.ExitMode(); _scanAt = -1;
        // Load invokes Clear internally: keep its pending slot until OnPostLoad.
    }
    private static void FinishLoad()
    {
        try
        {
            if (_pendingLoad != null) Targets.Load(_pendingLoad, Payroll.SaveHash(_pendingLoad));
        }
        finally { _pendingLoad = null; Loading = false; RequestScan(); }
    }
    public void Update()
    {
        FarmProduceWheel.Tick();
        if (!ModOptions.Screens.Value || Loading || _scanAt < 0 || Time.unscaledTime < _scanAt) return;
        _scanAt = -1;
        Plugin.Guard("Find existing farming modules", () =>
        {
            // Scene instances only, including inactive objects. Prefabs and
            // inventory ItemType assets aren't included in this typed scan.
            foreach (var module in Object.FindObjectsOfType<GreenhouseModuleView>(true)) Watch(module);
        });
    }
    internal static void Watch(GreenhouseModuleView module)
    {
        if (module == null || !module.gameObject.scene.IsValid()) return;
        var host = module.GetComponent<FarmScreenHost>() ?? module.gameObject.AddComponent<FarmScreenHost>();
        host.OnEnable();
    }
}

// Scene streaming can reuse pooled views. Recheck this module's ghost identity,
// not the entire world's objects, and never inherit a previous ghost's target.
public sealed class FarmScreenHost : MonoBehaviour
{
    private float _checkAt;
    private string? _identity;
    private bool _failed;
    public FarmScreenHost(IntPtr pointer) : base(pointer) { }
    public void OnEnable() { _checkAt = 0; _identity = null; _failed = false; }
    public void Update()
    {
        if (!ModOptions.Screens.Value || _failed || FarmScreens.Loading || Time.unscaledTime < _checkAt) return;
        _checkAt = Time.unscaledTime + 0.5f;
        try
        {
            var module = GetComponent<GreenhouseModuleView>();
            var held = GetComponent<HoldableEntity>();
            if (module == null || !module.IsInitialized || module.MyGhost?.ModuleType == null ||
                held != null && (held.IsHeld || held.IsBeingPlaced)) return;
            var identity = FarmScreen.TargetKey(module);
            if (identity == _identity) return;
            FarmLabelPreview.Attach(module);
            _identity = identity;
        }
        catch (Exception e) { _failed = true; Plugin.Logger.LogError("Attach loaded farm screen: " + e); }
    }
}

public sealed class FarmScreen : MonoBehaviour
{
    internal static readonly HashSet<FarmScreen> Active = new();
    internal GreenhouseModuleView Module = null!;
    internal MeshRenderer Display = null!;
    internal float Aspect;
    private string? _identity;
    private float _interactionCheck;
    private string? _displayedId;
    public FarmScreen(IntPtr pointer) : base(pointer) { }
    public void OnEnable() => Active.Add(this);
    public void OnDisable() => Active.Remove(this);
    public void OnDestroy() => Active.Remove(this);
    [HideFromIl2Cpp]
    internal bool Hit(Ray ray, float maximum, out float distance)
    {
        distance = 0;
        if (!CanUse()) return false;
        var transform = Display.transform;
        var face = Display.GetComponent<MeshFilter>().sharedMesh.bounds;
        var origin = transform.InverseTransformPoint(ray.origin);
        var direction = transform.InverseTransformVector(ray.direction);
        if (Mathf.Abs(direction.z) < 0.000001f) return false;
        distance = (face.center.z - origin.z) / direction.z;
        if (distance < 0 || distance > maximum) return false;
        var point = origin + direction * distance;
        return point.x >= face.min.x && point.x <= face.max.x && point.y >= face.min.y && point.y <= face.max.y;
    }
    internal static string TargetKey(GreenhouseModuleView module) => module.MyGhost.Id + ":" + module.MyGhost.ModuleType.Guid;
    [HideFromIl2Cpp]
    internal void Bind(GreenhouseModuleView module, MeshRenderer display, float aspect)
    {
        Module = module; Display = display; Aspect = aspect;
        Refresh();
    }
    [HideFromIl2Cpp]
    internal ItemType[] Choices() => (Module.MyGhost?.ModuleType ?? Module.GetComponent<ItemEntity>()?.Data)?.Plants?
        .ToArray().Where(p => p != null).DistinctBy(p => p.Guid)
        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() ?? Array.Empty<ItemType>();
    [HideFromIl2Cpp]
    internal string? SelectedId => Module.MyGhost == null ? null : FarmScreens.Targets.Get(TargetKey(Module));
    [HideFromIl2Cpp]
    internal void Refresh()
    {
        if (Module == null || Module.MyGhost?.ModuleType == null) return;
        _identity = TargetKey(Module);
        var item = FarmScreenApi.GetDisplayedProduce(Module);
        Display.sharedMaterial = FarmScreenPicture.Get(item?.Icon, Aspect);
        _displayedId = item?.Guid;
    }
    public void Update()
    {
        if (Time.unscaledTime < _interactionCheck) return;
        _interactionCheck = Time.unscaledTime + 0.1f;
        // CanInteract shares its tiny native body with unrelated game methods;
        // use the component's active flag instead of detouring that shared body.
        GetComponent<Interactable>().IsActive = CanUse();
        if (!FarmScreens.Loading && Module != null && Module.MyGhost?.ModuleType != null &&
            TargetKey(Module) == _identity && SelectedId == FarmTargets.Auto)
        {
            var item = FarmScreenApi.GetDisplayedProduce(Module);
            if (item?.Guid != _displayedId)
                Plugin.Guard("Update automatic farm label", Refresh);
        }
    }
    [HideFromIl2Cpp]
    internal bool CanUse()
    {
        if (FarmScreens.Loading || Module == null || !Module.gameObject.activeInHierarchy || Module.MyGhost?.ModuleType == null ||
            TargetKey(Module) != _identity) return false;
        var held = Module.GetComponent<HoldableEntity>();
        if (held != null && (held.IsHeld || held.IsBeingPlaced)) return false;
        var camera = Camera.main;
        return camera != null && Vector3.Dot(Display.transform.TransformDirection(Vector3.back),
            camera.transform.position - Display.transform.position) > 0;
    }
}

[HarmonyPatch(typeof(GreenhouseModuleView), nameof(GreenhouseModuleView.Start))]
internal static class FarmScreensStartPatch
{
    [HarmonyPostfix] private static void Postfix(GreenhouseModuleView __instance) =>
        Plugin.Guard("Watch farming module", () => FarmScreens.Watch(__instance));
}

[HarmonyPatch(typeof(Interactable), "get_InteractionName")]
internal static class FarmScreenNamePatch
{
    [HarmonyPrefix] private static bool Prefix(Interactable __instance, ref string __result)
    {
        if (__instance.GetComponent<FarmScreen>() == null) return true;
        __result = Labels.Get(FarmScreenEditor.Editing ? "farm.editPosition" : "farm.changeTarget"); return false;
    }
}
[HarmonyPatch(typeof(Interactable), nameof(Interactable.DoInteraction))]
internal static class FarmScreenInteractPatch
{
    [HarmonyPrefix] private static bool Prefix(Interactable __instance)
    {
        var screen = __instance.GetComponent<FarmScreen>();
        if (screen == null) return true;
        Plugin.Guard("Interact with farm screen", () =>
        {
            if (!screen.CanUse()) return;
            if (FarmScreenEditor.Editing) FarmScreenEditor.Select(screen.transform, screen.Module);
            else FarmProduceWheel.Open(screen);
        });
        return false;
    }
}
[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Load))]
internal static class FarmTargetsLoadPatch
{
    [HarmonyPrefix] private static void Prefix(string saveName) => FarmScreens.BeginLoad(saveName);
}
[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Clear))]
internal static class FarmTargetsClearPatch
{
    [HarmonyPostfix] private static void Postfix() => Plugin.Guard("Clear farm targets", FarmScreens.Clear);
}
[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Save))]
internal static class FarmTargetsSavePatch
{
    [HarmonyPostfix] private static void Postfix(string saveName, bool __result)
    {
        if (__result) Plugin.Guard("Save farm targets", () => FarmScreens.Targets.Save(saveName, Payroll.SaveHash(saveName)));
    }
}

// Screen interaction is a ray/quad intersection, entirely outside physics.
// A module's coarse box may cover its inset screen; other obstacles still block it.
[HarmonyPatch(typeof(FocusRaycaster), nameof(FocusRaycaster.FindInteractable))]
internal static class FarmScreenFocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(FocusRaycaster __instance, ref IInteractable __result)
    {
        if (FarmScreens.Loading || __instance.raycastCamera == null || FarmScreen.Active.Count == 0) return;
        var found = __result;
        Plugin.Guard("Focus farm screen", () =>
        {
            var ray = __instance.raycastCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            var nearest = __instance.focusDistance;
            foreach (var screen in FarmScreen.Active)
            {
                if (screen == null || !screen.Hit(ray, nearest, out var distance)) continue;
                // Check all hits, since the owning module may be the first hit
                // while an unrelated obstacle is between its box and the screen.
                var blocked = false;
                foreach (var hit in Physics.RaycastAll(ray, distance, __instance.raycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<GreenhouseModuleView>() != screen.Module) { blocked = true; break; }
                if (blocked) continue;
                nearest = distance;
                found = screen.GetComponent<Interactable>().Cast<IInteractable>();
            }
        });
        __result = found;
    }
}
