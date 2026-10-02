using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using Nivalis;
using Nivalis.Localization;
using Nivalis.UI;
using Nivalis.UI.InGameMenu;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NivalisMods.HudOverhaul;

// Copy the native presentation, without its gameplay settings/fade controllers.
public sealed class ModSettingsMenu : MonoBehaviour
{
    private UIPanel? _panel;
    private readonly List<GameObject> _pages = new();
    private readonly List<Action> _refresh = new();
    private int _showFrame = -1;
    public ModSettingsMenu(IntPtr pointer) : base(pointer) { }
    public void Update() => Plugin.Guard("HUD settings menu", Tick);

    private void Tick()
    {
        if (_panel != null)
        {
            var group = _panel.GetComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = _panel.IsVisible;
        }
        var pressed = Application.isFocused && QuickActionBindings.Pressed(QuickActionBindings.MenuName);
        if (!pressed && _showFrame < 0 && (_panel == null || !_panel.IsVisible)) return;
        var playerManager = PlayerManager._instance;
        if (FarmScreens.Loading || playerManager == null || playerManager.LocalPlayer?.Character == null)
        { Close(); return; }
        if (_showFrame >= 0 && Time.frameCount >= _showFrame)
        {
            _showFrame = -1;
            if (_panel != null)
            {
                foreach (var refresh in _refresh) refresh();
                Trace("Showing panel");
                _panel.Show();
                Trace("Panel shown");
                var group = _panel.GetComponent<CanvasGroup>();
                group.alpha = 1; group.interactable = group.blocksRaycasts = true;
            }
        }
        if (!pressed) return;
        if (_panel != null && (_panel.IsVisible || _showFrame >= 0)) { Close(); return; }
        var manager = UIManager._instance;
        if (manager == null || !UIManager.IsVisible) return;
        for (var i = 0; i < manager._openPanels.Count; i++)
        {
            var panel = manager._openPanels[i];
            if (panel == null || !panel.gameObject.activeInHierarchy || !panel.IsVisible) continue;
            if (panel.requiresMouse || panel.TryCast<UIWindow>()?.IsOpen == true) return;
        }
        if (_panel == null) Create();
        _panel!.transform.SetAsLastSibling();
        _showFrame = Time.frameCount + 1;
    }

    private void Close()
    {
        _showFrame = -1;
        if (_panel != null)
        {
            if (_panel.IsVisible) _panel.Hide();
            var group = _panel.GetComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
        }
    }

    private static void Click(Button button, Action callback)
    {
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(callback));
        button.interactable = true;
    }

    // Write synchronously: a native crash can lose BepInEx's buffered log tail.
    private static void Trace(string step)
    {
        if (!ModOptions.VerboseLogging.Value) return;
        Plugin.Verbose("Settings menu: " + step);
        try
        {
            File.AppendAllText(ModStorage.FilePath("HUDOverhaul.settings-menu.log"),
                $"{DateTime.UtcNow:O} {step}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Create()
    {
        Trace("Locating native settings window");
        var source = Resources.FindObjectsOfTypeAll<SettingsPanel>().FirstOrDefault(s =>
            s != null && s.togglesGroup != null && s.togglesGroup.toggles.Length == 4);
        if (source == null) throw new InvalidOperationException("Native settings window unavailable.");
        var staging = new GameObject("HUDOverhaul.SettingsStaging"); staging.SetActive(false);
        GameObject? root = null;
        try
        {
            Trace("Cloning native settings window");
            root = Object.Instantiate(source.gameObject, staging.transform);
            Trace("Window cloned; collecting control templates");
            root.name = "HUDOverhaul.SettingsMenu"; root.SetActive(false);
            var original = root.GetComponent<SettingsPanel>();
            var oldController = original.togglesGroup;
            var tabs = oldController.toggles.ToArray();
            var oldPanels = oldController.panels.ToArray();
            var toggleSource = root.GetComponentsInChildren<ToggleSettingUI>(true).First(t => t.name == "P_DisableHeadBob");
            var toggleTemplate = Object.Instantiate(toggleSource.gameObject, staging.transform);
            var stepSource = root.transform.Find("FrameWrapper/GraphicsSettings/Scrollview/Viewport/Content/P_ResolutionSetting");
            var stepTemplate = Object.Instantiate(stepSource.gameObject, staging.transform);
            var scrollTemplate = Object.Instantiate(root.transform.Find("FrameWrapper/GameplaySettings/MainSettings/Scrollview").gameObject, staging.transform);
            Trace("Templates copied; removing native window controllers");
            Object.DestroyImmediate(oldController);
            Object.DestroyImmediate(original);
            foreach (var controller in root.GetComponentsInChildren<CanvasBehaviourManager>(true)) Object.DestroyImmediate(controller);
            foreach (var canvas in root.GetComponentsInChildren<NestedCanvas>(true)) Object.DestroyImmediate(canvas);
            foreach (var popupName in new[] { "ThreeButtonPopup", "FrameWrapper/ConfirmTextureQualityChangePopup", "FrameWrapper/ApplyingMessage" })
            {
                var extra = root.transform.Find(popupName);
                if (extra != null) Object.DestroyImmediate(extra.gameObject);
            }
            var canvasRoot = root.GetComponent<Canvas>() ?? root.AddComponent<Canvas>();
            canvasRoot.enabled = true; canvasRoot.overrideSorting = true; canvasRoot.sortingOrder = 200;
            var raycaster = root.GetComponent<GraphicRaycaster>() ?? root.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;
            var group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            group.alpha = 0; group.interactable = group.blocksRaycasts = false;
            _panel = root.AddComponent<UIPanel>();
            Trace("Replacement panel created");
            _panel._startVisible = false;
            _panel.requiresMouse = _panel.pauseTimeWhenOpen = _panel.pauseTimeCompletely = true;
            _panel.closeWithCancel = _panel.closeWithPause = true;
            var title = root.transform.Find("FrameWrapper/P_Element_TitleFrame/Title");
            foreach (var localized in title.GetComponents<LocalizedStaticUILabel>()) Object.DestroyImmediate(localized);
            title.GetComponent<TMP_Text>().text = Labels.Get("settings.tab");
            Click(root.transform.Find("FrameWrapper/P_Element_CloseBtn/CloseBtn").GetComponent<Button>(), Close);
            _pages.Clear(); _refresh.Clear();
            var categories = new[] { "features", "shopping", "farm", "controls" };
            var contents = new Transform[4];
            for (var i = 0; i < tabs.Length; i++)
            {
                Trace("Creating category " + categories[i]);
                var oldRect = oldPanels[i].GetComponent<RectTransform>();
                var rect = NativeUiParts.Rect("HUDOverhaul." + categories[i], oldRect.parent);
                rect.anchorMin = oldRect.anchorMin; rect.anchorMax = oldRect.anchorMax;
                rect.pivot = oldRect.pivot; rect.sizeDelta = oldRect.sizeDelta; rect.anchoredPosition = oldRect.anchoredPosition;
                Object.DestroyImmediate(oldPanels[i].gameObject);
                var scrollObject = Object.Instantiate(scrollTemplate, rect);
                var scrollRect = scrollObject.GetComponent<RectTransform>();
                scrollRect.anchorMin = Vector2.zero; scrollRect.anchorMax = Vector2.one;
                scrollRect.offsetMin = new Vector2(20, 15); scrollRect.offsetMax = new Vector2(-20, -15);
                var scroll = scrollObject.GetComponent<ScrollRect>();
                scroll.onValueChanged = new ScrollRect.ScrollRectEvent();
                for (var child = scroll.content.childCount - 1; child >= 0; child--) Object.DestroyImmediate(scroll.content.GetChild(child).gameObject);
                scroll.content.anchoredPosition = Vector2.zero;
                var contentLayout = scroll.content.GetComponent<VerticalLayoutGroup>();
                contentLayout.childControlWidth = true;
                contentLayout.childForceExpandWidth = true;
                contentLayout.childControlHeight = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.childAlignment = TextAnchor.UpperLeft;
                contents[i] = scroll.content;
                _pages.Add(rect.gameObject);
                scrollObject.SetActive(true);
                tabs[i].onValueChanged = new Toggle.ToggleEvent();
                tabs[i].interactable = true;
                tabs[i].SetIsOnWithoutNotify(i == 0);
                NativeUiParts.Relabel(tabs[i].gameObject, Labels.Get("settings.category." + categories[i]));
                var index = i;
                tabs[i].onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(on =>
                {
                    if (!on) return;
                    for (var j = 0; j < _pages.Count; j++) _pages[j].SetActive(j == index);
                })));
                rect.gameObject.SetActive(i == 0);
            }
            void Check(int page, string key, ConfigEntry<bool> entry)
            {
                Trace("Creating checkbox " + key);
                var clone = Object.Instantiate(toggleTemplate, contents[page]); clone.SetActive(false);
                var controller = clone.GetComponent<ToggleSettingUI>();
                var toggle = controller.toggle;
                Object.DestroyImmediate(controller);
                NativeUiParts.Relabel(clone, Labels.Get("settings.option." + key));
                toggle.onValueChanged = new Toggle.ToggleEvent();
                var unavailable = key == "quests" && !QuestPatchRegistration.Installed;
                toggle.interactable = !unavailable;
                toggle.SetIsOnWithoutNotify(!unavailable && entry.Value);
                toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(value =>
                {
                    if (!unavailable) entry.Value = value;
                })));
                _refresh.Add(() => toggle.SetIsOnWithoutNotify(!unavailable && entry.Value));
                clone.SetActive(true);
                if (unavailable)
                {
                    var font = toggleTemplate.GetComponentInChildren<TMP_Text>(true);
                    var note = NativeUiParts.Text(contents[page], font, QuestPatchRegistration.UnavailableText);
                    note.gameObject.name = "HUDOverhaul.QuestCompatibilityNote";
                    note.fontSize = 14;
                    var color = note.color; color.a *= .6f; note.color = color;
                    note.alignment = TextAlignmentOptions.Center;
                    note.enableWordWrapping = true;
                    note.overflowMode = TextOverflowModes.Overflow;
                    note.margin = new Vector4(12, 0, 12, 8);
                    var layout = note.gameObject.AddComponent<LayoutElement>();
                    layout.minHeight = 32; layout.flexibleHeight = 0;
                }
            }
            void Step(int page, string key, ConfigEntry<int> entry, int min, int max, int step)
            {
                Trace("Creating number control " + key);
                var clone = Object.Instantiate(stepTemplate, contents[page]); clone.SetActive(false);
                Object.DestroyImmediate(clone.GetComponent<ResolutionSettingUI>());
                NativeUiParts.Relabel(clone, Labels.Get("settings.option." + key));
                var value = clone.transform.Find("ValueText").GetComponent<TMP_Text>();
                void Refresh() => value.text = entry.Value.ToString();
                Click(clone.transform.Find("ButtonLeft").GetComponent<Button>(), () => { entry.Value = Math.Max(min, entry.Value - step); Refresh(); });
                Click(clone.transform.Find("ButtonRight").GetComponent<Button>(), () => { entry.Value = Math.Min(max, entry.Value + step); Refresh(); });
                _refresh.Add(Refresh); Refresh(); clone.SetActive(true);
            }
            foreach (var (key, entry) in new[] {
                ("search", ModOptions.Search), ("sorting", ModOptions.Sorting), ("quickActions", ModOptions.QuickActions),
                ("staff", ModOptions.Staff), ("quests", ModOptions.Quests), ("scan", ModOptions.Scan),
                ("conditions", ModOptions.Conditions), ("reviewTime", ModOptions.ReviewTime), ("fastReviews", ModOptions.FastReviews),
                ("sales", ModOptions.Sales), ("ingredients", ModOptions.Ingredients) }) Check(0, key, entry);
            Check(0, "verbose", ModOptions.VerboseLogging);
            Step(0, "debounce", ModOptions.DebounceMs, 0, 1000, 50);
            // Reuse the native settings selector row, with named choices.
            var modeRow = Object.Instantiate(stepTemplate, contents[1]);
            Object.DestroyImmediate(modeRow.GetComponent<ResolutionSettingUI>());
            NativeUiParts.Relabel(modeRow, Labels.Get("settings.option.venueStock"));
            var modeText = modeRow.transform.Find("ValueText").GetComponent<TMP_Text>();
            void RefreshMode() => modeText.text = Labels.Get(ModOptions.DeductVenueStock.Value ? "shopping.stock.deduct" : "shopping.stock.ignore");
            void SwitchMode() { ModOptions.DeductVenueStock.Value = !ModOptions.DeductVenueStock.Value; RefreshMode(); }
            Click(modeRow.transform.Find("ButtonLeft").GetComponent<Button>(), SwitchMode);
            Click(modeRow.transform.Find("ButtonRight").GetComponent<Button>(), SwitchMode);
            _refresh.Add(RefreshMode); RefreshMode(); modeRow.SetActive(true);
            Step(1, "stockDays", ModOptions.StockDays, 1, 14, 1);
            Check(2, "screens", ModOptions.Screens);
            Check(2, "screenEditing", FarmScreenEditor._editing!);
            // Read-only reference, inside the existing Controls scroll area.
            Trace("Creating shortcut reference");
            var shortcutFont = toggleTemplate.GetComponentInChildren<TMP_Text>(true);
            var shortcutTitle = NativeUiParts.Text(contents[3], shortcutFont, Labels.Get("settings.editorShortcuts.title"));
            shortcutTitle.gameObject.name = "HUDOverhaul.EditorShortcutsTitle";
            shortcutTitle.fontSize = 28;
            shortcutTitle.alignment = TextAlignmentOptions.TopLeft;
            shortcutTitle.margin = new Vector4(8, 20, 8, 8);
            var titleLayout = shortcutTitle.gameObject.AddComponent<LayoutElement>();
            titleLayout.minHeight = titleLayout.preferredHeight = 64;
            titleLayout.flexibleHeight = 0;
            var shortcuts = NativeUiParts.Text(contents[3], shortcutFont, Labels.Get("settings.editorShortcuts.body"));
            shortcuts.gameObject.name = "HUDOverhaul.EditorShortcuts";
            shortcuts.fontSize = 24;
            shortcuts.alignment = TextAlignmentOptions.TopLeft;
            shortcuts.enableWordWrapping = true;
            shortcuts.overflowMode = TextOverflowModes.Overflow;
            shortcuts.margin = new Vector4(8, 0, 8, 16);
            contents[3].GetComponent<VerticalLayoutGroup>().spacing = 12;
            // A hidden source canvas may have disabled its graphics, layout and
            // input behaviours. The clone no longer has that manager to restore
            // them, so activate its remaining presentation components explicitly.
            foreach (var navigation in root.GetComponentsInChildren<ManualUINavigation>(true)) Object.DestroyImmediate(navigation);
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                var navigation = selectable.navigation; navigation.mode = Navigation.Mode.Automatic; selectable.navigation = navigation;
            }
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                Trace("Enabling " + behaviour.GetIl2CppType().FullName + " on " + behaviour.name);
                behaviour.enabled = true;
            }
            _panel.firstSelected = tabs[0].gameObject;
            Trace("Attaching panel to game canvas");
            root.transform.SetParent(UIManager._instance.FirstChildCanvas.transform, false);
            Trace("Activating panel");
            root.SetActive(true);
            Trace("Panel activated");
        }
        catch (Exception exception) { Trace("Creation failed: " + exception); if (root != null) Object.Destroy(root); _panel = null; _pages.Clear(); _refresh.Clear(); throw; }
        finally { Trace("Scheduling template cleanup"); Object.Destroy(staging); }
    }
}
