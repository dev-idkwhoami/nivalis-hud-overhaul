using HarmonyLib;
using Nivalis;
using Nivalis.UI;
using TMPro;
using UnityEngine;
using Nivalis.InventorySystem;
using Nivalis.CraftingSystem;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace NivalisMods.HudOverhaul;

// Owns the two wheel modes. Native click handling closes the wheel after its
// callback, so item actions run on the following frame, after that cleanup.
public sealed class QuickActionsPreview : MonoBehaviour
{
    private static RadialMenuUI? _wheel;
    private static QuickSlots? _slots;
    private static bool _assigning;
    private static string? _pendingAssignment;
    private static string? _pendingUse;
    private static IntPtr _pendingPlayer;
    private static int _afterFrame;
    private static float _deadline;
    private static InventoryItemDisplayUI? _useAdapter;
    public QuickActionsPreview(IntPtr pointer) : base(pointer) { }

    internal static void Initialize()
    {
        _slots = new QuickSlots(ModStorage.FilePath(QuickSlots.FileName),
            message => Plugin.Logger.LogWarning(message));
    }

    public void Update()
    {
        Plugin.Guard("Update quick actions", Tick);
        Plugin.Guard("Sample equipment transform", EquipmentTrace.Tick);
    }

    private static void Tick()
    {
        if ((_pendingAssignment != null || _pendingUse != null) && Time.frameCount >= _afterFrame)
        {
            var player = PlayerManager.Instance?.LocalPlayer?.Character;
            if (player == null || player.Pointer != _pendingPlayer)
            {
                _pendingAssignment = _pendingUse = null;
                return;
            }
            if (CanOpen(false))
            {
                if (_pendingUse != null)
                {
                    var id = _pendingUse;
                    _pendingUse = null;
                    Use(id);
                }
                else
                {
                    var id = _pendingAssignment;
                    _pendingAssignment = null;
                    Open(id);
                }
                return;
            }
            if (Time.unscaledTime > _deadline)
            {
                _pendingAssignment = _pendingUse = null;
                Plugin.Logger.LogWarning("Quick Actions canceled: another UI or player activity is still blocking the action.");
            }
        }
        if (!ModOptions.QuickActions.Value || !QuickActionBindings.Pressed()) return;
        if (_wheel != null && _wheel.IsOpen) { _wheel.Close(); return; }
        if (_pendingAssignment == null && _pendingUse == null && CanOpen(true)) Open(null);
    }

    internal static bool Eligible(ItemType? item) => ModOptions.QuickActions.Value && item != null &&
        (item.IsEquippable || item.IsUseable || item.IsConsumable || item.IsMeal);

    private static bool Blocked(string reason, bool log)
    {
        if (log) Plugin.Verbose($"Quick actions shortcut ignored: {reason}");
        return false;
    }

    private static bool CanOpen(bool log)
    {
        var wheel = RadialMenuUI.instance;
        var player = PlayerManager.Instance?.LocalPlayer?.Character;
        if (wheel == null) return Blocked("native radial menu is not initialized", log);
        if (wheel.IsOpen) return Blocked("another radial menu is open", log);
        if (player == null || !player.CanEquipItem) return Blocked("player is unavailable or busy", log);
        var manager = UIManager.Instance;
        if (manager == null || !UIManager.IsVisible) return Blocked("root UI canvas is unavailable", log);
        for (var i = 0; i < manager._openPanels.Count; i++)
        {
            var panel = manager._openPanels[i];
            if (panel == null || !panel.gameObject.activeInHierarchy || !panel.IsVisible) continue;
            if (panel.requiresMouse || panel.TryCast<UIWindow>()?.IsOpen == true)
                return Blocked($"visible panel {panel.name}", log);
        }
        return true;
    }

    internal static void BeginAssignment(InventoryItemDisplayUI row)
    {
        var item = row.ItemType;
        var player = PlayerManager.Instance?.LocalPlayer?.Character;
        if (!Eligible(item) || player == null || FindStack(item!.Guid) == null) return;
        _pendingAssignment = item.Guid;
        _pendingUse = null;
        _pendingPlayer = player.Pointer;
        _afterFrame = Time.frameCount + 1;
        _deadline = Time.unscaledTime + 3;
        // Snapshot first: Close modifies the manager's registry. Parent windows
        // handle their own subpanels and release pause/cursor locks normally.
        var windows = new List<UIWindow>();
        var panels = UIManager.Instance._openPanels;
        for (var i = 0; i < panels.Count; i++)
        {
            var window = panels[i]?.TryCast<UIWindow>();
            if (window != null && window.IsOpen) windows.Add(window);
        }
        for (var i = windows.Count - 1; i >= 0; i--)
            if (windows[i].IsOpen) windows[i].Close();
    }

    private static void Open(string? assigning)
    {
        var wheel = RadialMenuUI.instance;
        if (assigning != null && FindStack(assigning) == null) return;
        wheel.Clear();
        for (var i = 0; i < QuickSlots.Count; i++)
        {
            var slot = i;
            var id = _slots![slot];
            var stack = id == null ? null : FindStack(id);
            var item = stack?.Type ?? (id == null ? null : ItemDatabase.Instance?.TryProvide(id)?.TryCast<ItemType>());
            var label = id == null
                ? Labels.Get(assigning == null ? "quickActions.emptySlot" : "quickActions.assignSlot").Replace("{slot}", (slot + 1).ToString())
                : item?.Name ?? Labels.Get("quickActions.missingItem");
            if (assigning == null && id != null && stack == null)
                label = Labels.Get("quickActions.unavailable").Replace("{item}", label);
            if (assigning == null && id != null)
                label = Labels.Get("quickActions.unassignHint").Replace("{item}", label);
            var enabled = assigning != null ? id == null : stack != null && Eligible(item);
            Il2CppSystem.Action? callback = null;
            if (enabled)
            {
                if (assigning != null)
                    callback = (Il2CppSystem.Action)(() => Plugin.Guard("Assign quick slot", () =>
                    {
                        if (FindStack(assigning) != null && _slots.Assign(slot, assigning))
                            Plugin.Verbose($"Assigned quick slot {slot + 1}.");
                    }));
                else
                    callback = (Il2CppSystem.Action)(() => QueueUse(id!));
            }
            wheel.AddAction(item?.Icon!, label, false, callback!, enabled);
        }
        wheel.AddCloseAction();
        _assigning = assigning != null;
        _wheel = wheel;
        try { wheel.Open(); }
        catch { _wheel = null; wheel.Clear(); throw; }
    }

    private static ItemStack? FindStack(string id)
    {
        var inventory = PlayerManager.Instance?.LocalPlayer?.Inventory?.Items;
        if (inventory == null) return null;
        // Avoid the boxed IL2CPP IEnumerator interface: its MoveNext path
        // throws a version-mismatch exception in this game's interop build.
        // Walk the current inventory's native nodes without an enumerator.
        var node = inventory._items.First;
        var remaining = inventory._items.Count;
        while (node != null && remaining-- > 0)
        {
            var stack = node.Value;
            if (stack != null && stack.StackCount > 0 && stack.Type?.Guid == id) return stack;
            node = node.Next;
        }
        return null;
    }

    private static void QueueUse(string id)
    {
        var player = PlayerManager.Instance?.LocalPlayer?.Character;
        if (player == null) return;
        _pendingUse = id;
        _pendingPlayer = player.Pointer;
        _afterFrame = Time.frameCount + 1;
        _deadline = Time.unscaledTime + 3;
    }

    private static void Use(string id)
    {
        if (!CanOpen(true)) return;
        var stack = FindStack(id);
        if (stack == null || !Eligible(stack.Type)) return;
        if (_useAdapter == null)
        {
            // This native handler only requires _stack and an optional parent
            // window. An inactive adapter avoids Awake/UI listeners entirely.
            var root = new GameObject("HUDOverhaul.InventoryActionAdapter");
            root.SetActive(false);
            _useAdapter = root.AddComponent<InventoryItemDisplayUI>();
            UnityEngine.Object.DontDestroyOnLoad(root);
        }
        _useAdapter._stack = stack;
        QuickEquipmentPose.UsingItem = true;
        try { _useAdapter.HoldButtonListener(); }
        finally
        {
            QuickEquipmentPose.UsingItem = false;
            _useAdapter._stack = null!;
        }
    }

    internal static bool HandleRightClick(RadialMenuUI wheel)
    {
        if (_wheel == null || _wheel.Pointer != wheel.Pointer || !wheel.IsOpen || _assigning ||
            Mouse.current?.rightButton.wasPressedThisFrame != true) return false;
        // Native hover includes inactive sectors, so missing inventory items can
        // still be removed. Do not let a simultaneous left click use the item.
        // Native UpdateAction runs before UpdateHover; sample the current
        // pointer position so a move-and-click cannot remove the previous slot.
        wheel.UpdateHover();
        var hovered = wheel.hovered;
        if (hovered == null) return true;
        for (var slot = 0; slot < QuickSlots.Count && slot < wheel.items.Count; slot++)
        {
            if (wheel.items[slot].Pointer != hovered.Pointer) continue;
            if (_slots!.Unassign(slot))
            {
                Plugin.Verbose($"Unassigned quick slot {slot + 1}.");
                wheel.Close();
            }
            break;
        }
        return true;
    }

    internal static void Closed(RadialMenuUI wheel)
    {
        if (_wheel != null && _wheel.Pointer == wheel.Pointer) _wheel = null;
    }
}

public sealed class QuickAssignButton : MonoBehaviour
{
    private GameObject? _button;
    private InventoryItemDisplayUI? _row;
    public QuickAssignButton(IntPtr pointer) : base(pointer) { }

    internal void Refresh(InventoryItemDisplayUI row)
    {
        _row = row;
        var eligible = QuickActionsPreview.Eligible(row.ItemType);
        if (_button == null && eligible && row.holdButton != null) Create(row.holdButton);
        if (_button != null) _button.SetActive(eligible);
    }

    private void Create(Button source)
    {
        var root = new GameObject("HUDOverhaul.QuickAssign");
        root.SetActive(false);
        var rect = root.AddComponent<RectTransform>();
        rect.SetParent(source.transform.parent, false);
        rect.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        rect.sizeDelta = new Vector2(90, 35);
        var image = root.AddComponent<Image>();
        var original = source.GetComponent<Image>();
        if (original != null) { image.sprite = original.sprite; image.type = original.type; }
        image.color = Color.white;
        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = true;
        button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => Plugin.Guard("Open assignment wheel", () =>
        {
            if (_row != null && _row.gameObject.activeInHierarchy) QuickActionsPreview.BeginAssignment(_row);
        })));
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = new Color(0.25f, 0.25f, 0.25f, 1);
        colors.highlightedColor = new Color(0.55f, 0.40f, 0.22f, 1);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.68f, 0.49f, 0.26f, 1);
        colors.disabledColor = new Color(0.20f, 0.20f, 0.20f, 0.6f);
        colors.colorMultiplier = 1;
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var textRoot = new GameObject("Label");
        var textRect = textRoot.AddComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 0);
        textRect.offsetMax = new Vector2(-8, 0);
        var text = textRoot.AddComponent<TextMeshProUGUI>();
        var originalText = source.GetComponentInChildren<TextMeshProUGUI>(true);
        if (originalText != null) { text.font = originalText.font; text.fontSize = originalText.fontSize; }
        else text.fontSize = 20;
        text.text = Labels.Get("quickActions.assign");
        text.color = new Color(0.9f, 0.76f, 0.53f, 1);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        var layout = root.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = Mathf.Max(90, text.GetPreferredValues(text.text).x + 20);
        layout.minHeight = layout.preferredHeight = 35;
        layout.flexibleWidth = 0;
        _button = root;
    }
}

[HarmonyPatch(typeof(RadialMenuUI), nameof(RadialMenuUI.Close))]
internal static class QuickActionsClosedPatch
{
    [HarmonyPostfix]
    private static void Postfix(RadialMenuUI __instance) => QuickActionsPreview.Closed(__instance);
}

[HarmonyPatch(typeof(PlayerInventoryUI), nameof(PlayerInventoryUI.RefreshInventoryDisplay))]
internal static class QuickAssignInventoryPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInventoryUI __instance) => Plugin.Guard("Refresh inventory Assign buttons", () =>
    {
        var list = __instance.inventoryItems;
        for (var i = 0; i < list.DisplayedCount; i++)
        {
            var row = list.GetItem(i).GameObject.GetComponent<InventoryItemDisplayUI>();
            if (row == null) continue;
            var control = row.GetComponent<QuickAssignButton>() ?? row.gameObject.AddComponent<QuickAssignButton>();
            control.Refresh(row);
        }
    });
}

[HarmonyPatch(typeof(RadialMenuUI), nameof(RadialMenuUI.UpdateAction))]
internal static class QuickActionsRightClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RadialMenuUI __instance)
    {
        var handled = false;
        Plugin.Guard("Unassign quick slot", () => handled = QuickActionsPreview.HandleRightClick(__instance));
        return !handled;
    }
}
