using System.Text.Json;
using BepInEx;
using NivalisMods.ModCompanion.Api;
using Nivalis;
using Nivalis.InventorySystem;
using Nivalis.UI;
using Nivalis.UI.Greenhouse;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NivalisMods.HudOverhaul;

// Opt-in layout calibration. Profiles describe model-local transforms,
// not objects in a save or item ownership.
public sealed class FarmScreenEditor : MonoBehaviour
{
    internal const string DefaultScreen = "f8a47c8e-fb42-441b-a269-1a6214b05c68";
    private static readonly string FilePath = Path.Combine(ModStorage.Root, "HUDOverhaul.farm-screen-preview.json");
    private static Settings? _settings;
    private static Settings? _defaults;
    internal static Setting<bool>? _editing;
    private static bool _placementMode;
    private static TMP_Text? _notice;
    internal static bool Editing => _editing?.Value == true && _placementMode;
    internal static void ExitMode() { Cancel(); _placementMode = false; }
    private static Transform? _target;
    private static string _key = "", _name = "";
    private static PoseData? _before;
    private static bool _failed;
    public FarmScreenEditor(IntPtr pointer) : base(pointer) { }

    internal static void Initialize(SettingsCategory farm)
    {
        _editing = farm.Toggle("PlacementEditing", Labels.Get("settings.option.screenEditing"), false,
            "Allow F4 to toggle screen placement editing. Editing starts off; click a screen in editing mode to select it. Enter saves; Backspace cancels.");
        _editing.Changed += _ => { if (!_editing.Value) ExitMode(); };
    }
    private static Settings Defaults
    {
        get
        {
            if (_defaults != null) return _defaults;
            using var stream = typeof(FarmScreenEditor).Assembly.GetManifestResourceStream("HudOverhaul.FarmScreenDefaults.json")
                ?? throw new InvalidDataException("Bundled farm screen placements missing.");
            var defaults = JsonSerializer.Deserialize<Settings>(stream)
                ?? throw new InvalidDataException("Bundled farm screen placements invalid.");
            if (defaults.Placements == null || defaults.Placements.Values.Any(p => p == null || !p.Valid))
                throw new InvalidDataException("Invalid bundled farm screen pose.");
            return _defaults = defaults;
        }
    }

    private sealed class Settings
    {
        public string ScreenItemId { get; set; } = DefaultScreen;
        public Dictionary<string, PoseData> Placements { get; set; } = new();
    }
    private sealed class PoseData
    {
        public float[] Position { get; set; } = Array.Empty<float>();
        public float[] Rotation { get; set; } = Array.Empty<float>();
        public float[] Scale { get; set; } = Array.Empty<float>();
        internal bool Valid => new[] { Position, Rotation, Scale }.All(a => a != null && a.Length == 3 && a.All(float.IsFinite))
            && Scale.All(v => v > 0 && v < 1000);
        internal static PoseData Capture(Transform t) => new()
        {
            Position = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
            Rotation = new[] { t.localEulerAngles.x, t.localEulerAngles.y, t.localEulerAngles.z },
            Scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z }
        };
        internal void Apply(Transform t)
        {
            if (!Valid) throw new InvalidDataException("Invalid farm screen placement.");
            t.localPosition = new Vector3(Position[0], Position[1], Position[2]);
            t.localEulerAngles = new Vector3(Rotation[0], Rotation[1], Rotation[2]);
            t.localScale = new Vector3(Scale[0], Scale[1], Scale[2]);
        }
    }
    private static Settings Data
    {
        get
        {
            if (_settings != null) return _settings;
            _settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) : new Settings { ScreenItemId = Defaults.ScreenItemId };
            if (_settings == null || _settings.Placements == null || string.IsNullOrWhiteSpace(_settings.ScreenItemId))
            { _settings = null; throw new InvalidDataException("Invalid farm screen settings."); }
            return _settings;
        }
    }
    // Pose files use the original test TV as their coordinate reference.
    // The replacement visuals are aligned to that reference in FarmLabelPreview.
    internal static string ScreenId => "42623e74-f781-48d3-9c16-faa74dcd102d";
    private static void Save()
    {
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
    internal static string ModelKey(GreenhouseModuleView module) =>
        (module.GetComponent<ItemEntity>()?.Data?.Guid ?? module.name.Replace("(Clone)", "").Trim()) + ":" + DefaultScreen;
    internal static bool HasSaved(string key) => Data.Placements.ContainsKey(key) || Defaults.Placements.ContainsKey(key);
    internal static void ApplySaved(Transform target, string key)
    {
        // User overrides win; new installations use the bundled calibration.
        if (Data.Placements.TryGetValue(key, out var pose) || Defaults.Placements.TryGetValue(key, out pose))
            pose.Apply(target);
    }
    internal static void Select(Transform target, GreenhouseModuleView module)
    {
        Cancel();
        if (!Editing) return;
        _target = target;
        _key = ModelKey(module);
        _name = module.GetComponent<ItemEntity>()?.Data?.Name ?? module.name;
        _before = PoseData.Capture(target);
        _failed = false;
    }
    private static void Cancel()
    {
        if (_target != null && _before != null) _before.Apply(_target);
        _target = null;
        _before = null;
    }
    private static bool CanEdit() => Editing && _target != null && _target.gameObject.activeInHierarchy && GameplayAvailable();
    private static bool GameplayAvailable()
    {
        if (!Application.isFocused) return false;
        var ui = UIManager.Instance;
        if (ui == null || !UIManager.IsVisible || PlayerManager.Instance?.LocalPlayer?.Character == null) return false;
        for (var i = 0; i < ui._openPanels.Count; i++)
        {
            var panel = ui._openPanels[i];
            if (panel == null || !panel.gameObject.activeInHierarchy || !panel.IsVisible) continue;
            if (panel.requiresMouse || panel.TryCast<UIWindow>()?.IsOpen == true) return false;
        }
        return true;
    }
    public void Update()
    {
        try { Tick(); RefreshNotice(); }
        catch (Exception e) { _failed = true; Plugin.Logger.LogError("Farm screen editor: " + e); }
    }
    private static void Tick()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (QuickActionBindings.Pressed(QuickActionBindings.EditName) && ModOptions.Screens.Value && GameplayAvailable() && _editing?.Value == true)
        {
            Cancel();
            _placementMode = !_placementMode;
            _failed = false;
        }
        if (_failed || !CanEdit()) return;
        if (keyboard.backspaceKey.wasPressedThisFrame) { Cancel(); return; }
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            var pose = PoseData.Capture(_target!);
            Data.Placements.TryGetValue(_key, out var old);
            Data.Placements[_key] = pose;
            try { Save(); }
            catch { if (old == null) Data.Placements.Remove(_key); else Data.Placements[_key] = old; throw; }
            if (Plugin.IsVerbose) Plugin.Verbose($"Farm screen placement saved: {_name}; key={_key}; {JsonSerializer.Serialize(pose)}");
            _target = null; _before = null;
            return;
        }
        if (!keyboard.leftAltKey.isPressed && !keyboard.rightAltKey.isPressed) return;
        var factor = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 0.2f : 1f;
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f) * factor;
        var move = new Vector3(
            (keyboard.numpad6Key.isPressed ? 1 : 0) - (keyboard.numpad4Key.isPressed ? 1 : 0),
            (keyboard.numpad9Key.isPressed ? 1 : 0) - (keyboard.numpad3Key.isPressed ? 1 : 0),
            (keyboard.numpad8Key.isPressed ? 1 : 0) - (keyboard.numpad5Key.isPressed ? 1 : 0));
        // Directions follow the machine's axes; normalize parent scale to metres.
        var parentScale = _target!.parent.lossyScale;
        _target.localPosition += new Vector3(move.x / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
            move.y / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f), move.z / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f)) * (0.15f * dt);
        var turn = new Vector3(
            (keyboard.numpadMultiplyKey.isPressed ? 1 : 0) - (keyboard.numpadDivideKey.isPressed ? 1 : 0),
            (keyboard.numpad7Key.isPressed ? 1 : 0) - (keyboard.numpad1Key.isPressed ? 1 : 0),
            (keyboard.numpadPeriodKey.isPressed ? 1 : 0) - (keyboard.numpad0Key.isPressed ? 1 : 0));
        _target.localRotation = Quaternion.Euler(turn * (30 * dt)) * _target.localRotation;
        var grow = (keyboard.numpadPlusKey.isPressed ? 1 : 0) - (keyboard.numpadMinusKey.isPressed ? 1 : 0);
        if (grow != 0)
        {
            var next = _target.localScale * Mathf.Pow(2, grow * dt);
            if (next.x > 0.0001f && next.y > 0.0001f && next.z > 0.0001f && next.x < 100 && next.y < 100 && next.z < 100)
                _target.localScale = next;
        }
    }
    private static void RefreshNotice()
    {
        var visible = Editing && GameplayAvailable();
        if (_notice == null && visible)
        {
            var source = Resources.FindObjectsOfTypeAll<GreenhouseConsoleUI>()
                .FirstOrDefault(w => w != null && w.transform.Find("GreenhouseTitle") != null);
            var heading = source?.transform.Find("GreenhouseTitle").GetComponent<TMP_Text>();
            if (heading == null) return;
            var root = new GameObject("HUDOverhaul.FarmEditingNotice");
            var rect = root.AddComponent<RectTransform>();
            rect.SetParent(UIManager.Instance.FirstChildCanvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-32, 32);
            rect.sizeDelta = new Vector2(420, 72);
            _notice = root.AddComponent<TextMeshProUGUI>();
            _notice.font = heading.font;
            _notice.fontSize = 22;
            _notice.color = heading.color;
            _notice.alignment = TextAlignmentOptions.BottomRight;
            _notice.enableWordWrapping = true;
            _notice.raycastTarget = false;
            _notice.text = Labels.Get("farm.editNoticeKey").Replace("{key}", QuickActionBindings.Display(QuickActionBindings.EditName));
        }
        if (_notice != null && visible)
            _notice.text = Labels.Get("farm.editNoticeKey").Replace("{key}", QuickActionBindings.Display(QuickActionBindings.EditName));
        if (_notice != null && _notice.gameObject.activeSelf != visible)
            _notice.gameObject.SetActive(visible);
    }
}
