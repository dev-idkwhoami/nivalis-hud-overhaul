using BepInEx;
using HarmonyLib;
using Nivalis;
using Il2CppInterop.Runtime.Attributes;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using Nivalis.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NivalisMods.HudOverhaul;

[HarmonyPatch(typeof(VenueStaffOverviewTab), nameof(VenueStaffOverviewTab.RefreshStaffList))]
internal static class StaffOrderingPatch
{
    [HarmonyPrefix]
    private static void Prefix(VenueStaffOverviewTab __instance) => Plugin.Guard("Suspend staff controls", () =>
        __instance.GetComponent<StaffOrdering>()?.BeforeRefresh());

    [HarmonyPostfix]
    private static void Postfix(VenueStaffOverviewTab __instance, Venue __0) => Plugin.Guard("Staff display order", () =>
    {
        if (!ModOptions.Staff.Value) return;
        var order = __instance.GetComponent<StaffOrdering>() ?? __instance.gameObject.AddComponent<StaffOrdering>();
        order.Refresh(__instance, __0);
    });
}

public sealed class StaffOrdering : MonoBehaviour
{
    private static StaffOrder? _saved;
    private VenueStaffOverviewTab _tab = null!;
    private string _venue = "";
    private readonly List<StaffOrderButtons> _rows = new();
    private readonly Il2CppSystem.Collections.Generic.List<RaycastResult> _hits = new();
    private PointerEventData? _pointer;
    private EventSystem? _events;
    private bool _failed = true;
    private bool _mouseMode = true;

    public StaffOrdering(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void BeforeRefresh()
    {
        _failed = true;
        foreach (var row in _rows) if (row != null) row.Hide();
        _rows.Clear();
        _venue = "";
    }

    [HideFromIl2Cpp]
    internal void Refresh(VenueStaffOverviewTab tab, Venue venue)
    {
        _tab = tab;
        _venue = venue.Guid;
        _failed = true;
        _saved ??= new StaffOrder(ModStorage.FilePath(StaffOrder.FileName),
            message => Plugin.Logger.LogWarning(message));
        _rows.Clear();
        var list = tab.staffList;
        for (var i = 0; i < list.DisplayedCount; i++)
        {
            var row = list.GetItem(i).GameObject.GetComponent<VenueStaffListItem>();
            if (row == null) continue;
            var controls = row.GetComponent<StaffOrderButtons>();
            // Pooled hire/locked rows can retain their previous Person reference.
            var worker = row.workersPanelParent.activeSelf && row._person != null;
            if (!worker)
            {
                controls?.Hide();
                continue;
            }
            controls ??= row.gameObject.AddComponent<StaffOrderButtons>();
            controls.Bind(this, row);
            _rows.Add(controls);
        }
        if (string.IsNullOrEmpty(_venue) || _rows.Any(row => string.IsNullOrEmpty(row.PersonId)))
            throw new InvalidOperationException("Staff order requires stable venue and employee IDs.");
        var ranks = _saved.Resolve(_venue, _rows.Select(row => row.PersonId));
        _rows.Sort((a, b) => Array.IndexOf(ranks, a.PersonId).CompareTo(Array.IndexOf(ranks, b.PersonId)));
        Apply();
        _failed = false;
        Plugin.Verbose($"Staff order ready: {_rows.Count} employees at {venue.EntryName}.");
    }

    [HideFromIl2Cpp]
    internal void Move(StaffOrderButtons row, int direction)
    {
        Plugin.Guard("Move staff card", () =>
        {
            if (_failed || _tab._forVenue == null || _tab._forVenue.Guid != _venue ||
                row.Card._person == null || row.Card._person.Guid != row.PersonId) return;
            var index = _rows.IndexOf(row);
            var target = index + direction;
            if (index < 0 || target < 0 || target >= _rows.Count) return;
            (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
            Apply();
            _saved!.Remember(_venue, _rows.Select(item => item.PersonId).ToArray());
            // Stop inertia from moving the list during a reorder.
            var scroll = _tab.staffList.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null) scroll.velocity = Vector2.zero;
        });
    }

    [HideFromIl2Cpp]
    private void Apply()
    {
        var list = _tab.staffList;
        var first = list.DisplayedCount > 0 ? list.GetItem(0).GameObject.GetComponent<Selectable>() : null;
        var last = list.DisplayedCount > 0 ? list.GetItem(list.DisplayedCount - 1).GameObject.GetComponent<Selectable>() : null;
        var above = first != null ? first.navigation.selectOnUp : null;
        var below = last != null ? last.navigation.selectOnDown : null;
        if (above != null && above.transform.IsChildOf(list.transform)) above = null;
        if (below != null && below.transform.IsChildOf(list.transform)) below = null;
        var slots = new List<(int Index, int Sibling, Transform Parent)>();
        var displays = new Dictionary<string, IItemDisplayUI>();
        for (var i = 0; i < list.DisplayedCount; i++)
        {
            var display = list.GetItem(i);
            var row = display.GameObject.GetComponent<StaffOrderButtons>();
            if (row == null || !_rows.Contains(row)) continue;
            slots.Add((i, row.transform.GetSiblingIndex(), row.transform.parent));
            displays.Add(row.PersonId, display);
        }
        if (slots.Count != _rows.Count) throw new InvalidOperationException("Staff pool changed during reorder.");
        // Only replace occupied worker slots. Hire/locked placeholders keep their positions.
        for (var i = 0; i < _rows.Count; i++)
        {
            var slot = slots[i];
            var row = _rows[i];
            if (row.transform.parent != slot.Parent) row.transform.SetParent(slot.Parent, false);
            list._itemDisplayInstances[slot.Index] = displays[row.PersonId];
        }
        // Slots are in list order, which can differ from sibling order after a previous move.
        var siblings = slots.Select(slot => slot.Sibling).OrderBy(value => value).ToArray();
        for (var i = 0; i < _rows.Count; i++) _rows[i].transform.SetSiblingIndex(siblings[i]);
        for (var i = 0; i < _rows.Count; i++) _rows[i].SetBoundaries(i > 0, i + 1 < _rows.Count);
        list.UpdateUINavigationList(above, below);
        foreach (var row in _rows) row.Card.UpdateManualUINavigation();
        if (_rows.Count > 0)
            LayoutRebuilder.MarkLayoutForRebuild(_rows[0].transform.parent.GetComponent<RectTransform>());
    }

    public void Update()
    {
        if (_failed || _rows.Count == 0) return;
        try { UpdateVisibility(); }
        catch (Exception e)
        {
            _failed = true;
            foreach (var row in _rows) row.Hide();
            Plugin.Logger.LogError($"Staff hover controls: {e}");
        }
    }

    [HideFromIl2Cpp]
    private void UpdateVisibility()
    {
        var events = EventSystem.current;
        if (events == null) return;
        var mouse = Mouse.current;
        if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0 || mouse.leftButton.wasPressedThisFrame)) _mouseMode = true;
        if (Keyboard.current?.anyKey.wasPressedThisFrame == true ||
            (Gamepad.current != null && (Gamepad.current.dpad.ReadValue().sqrMagnitude > 0 ||
             Gamepad.current.leftStick.ReadValue().sqrMagnitude > .2f || Gamepad.current.buttonSouth.wasPressedThisFrame)))
            _mouseMode = false;
        Transform? hit = null;
        if (_mouseMode && mouse != null)
        {
            if (_pointer == null || _events != events)
            {
                _events = events;
                _pointer = new PointerEventData(events);
            }
            _pointer.position = mouse.position.ReadValue();
            _hits.Clear();
            events.RaycastAll(_pointer, _hits);
            if (_hits.Count > 0) hit = _hits[0].gameObject.transform;
        }
        var selected = events.currentSelectedGameObject;
        foreach (var row in _rows)
            row.SetVisible((hit != null && hit.IsChildOf(row.transform)) ||
                (!_mouseMode && selected != null && selected.transform.IsChildOf(row.transform)));
    }

    public void OnDisable()
    {
        foreach (var row in _rows) if (row != null) row.Hide();
    }
}

public sealed class StaffOrderButtons : MonoBehaviour
{
    internal VenueStaffListItem Card = null!;
    internal string PersonId = "";
    private StaffOrdering _owner = null!;
    private CanvasGroup? _group;
    private Button _up = null!;
    private Button _down = null!;
    private bool _canUp;
    private bool _canDown;

    public StaffOrderButtons(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void Bind(StaffOrdering owner, VenueStaffListItem card)
    {
        _owner = owner;
        Card = card;
        PersonId = card._person.Guid;
        if (_group == null)
        {
            var go = new GameObject("HUD Staff Order");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(card.workersPanelParent.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            _group = go.AddComponent<CanvasGroup>();
            _up = CreateButton(rect, true);
            _down = CreateButton(rect, false);
        }
        Hide();
    }

    [HideFromIl2Cpp]
    private Button CreateButton(RectTransform parent, bool up)
    {
        var go = new GameObject(up ? "Move employee up" : "Move employee down");
        // ManualUINavigation expects four serialized Optional objects even when
        // no direction is locked. Configure them before its first Awake.
        go.SetActive(false);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        // The original card is 72 units high; the fire button occupies the middle 30.
        // These fit in the free strip above/below it, outside the shift slider.
        rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
        rect.sizeDelta = new Vector2(18, 18);
        rect.anchoredPosition = new Vector2(Card.fireButton.GetComponent<RectTransform>().anchoredPosition.x, up ? 25 : -25);
        var background = go.AddComponent<Image>();
        background.color = new Color(.24f, .18f, .12f, .95f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(.5f, .5f, .5f, .5f);
        button.colors = colors;
        button.onClick.AddListener((UnityEngine.Events.UnityAction)(() => _owner.Move(this, up ? -1 : 1)));
        // Reuse the native shift-arrow sprite, rotated vertically. No font glyph dependency.
        var source = Card.timeSlotSlider.transform.Find("LeftArrow/Image")?.GetComponent<Image>();
        if (source == null || source.sprite == null) throw new InvalidOperationException("Staff arrow sprite missing.");
        var arrowGo = new GameObject("Arrow");
        var arrowRect = arrowGo.AddComponent<RectTransform>();
        arrowRect.SetParent(rect, false);
        arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(.5f, .5f);
        arrowRect.sizeDelta = new Vector2(10, 10);
        arrowRect.localRotation = Quaternion.Euler(0, 0, up ? 90 : -90);
        var arrow = arrowGo.AddComponent<Image>();
        arrow.sprite = source.sprite;
        arrow.color = new Color(1, .9f, .7f, 1);
        arrow.preserveAspect = true;
        arrow.raycastTarget = false;
        var navigation = go.AddComponent<ManualUINavigation>();
        navigation.lockSelectOnUp = new Optional<Selectable>(null!, false);
        navigation.lockSelectOnDown = new Optional<Selectable>(null!, false);
        navigation.lockSelectOnLeft = new Optional<Selectable>(null!, false);
        navigation.lockSelectOnRight = new Optional<Selectable>(null!, false);
        navigation._selectable = button;
        go.SetActive(true);
        // Check initialization before exposing the control to the native refresh loop.
        navigation.SetCustomNavigation(null, null, null, null);
        Card.internalNavigation.Add(navigation);
        return button;
    }

    [HideFromIl2Cpp]
    internal void SetBoundaries(bool up, bool down)
    {
        _canUp = up;
        _canDown = down;
        _up.interactable = up;
        _down.interactable = down;
        _up.transform.Find("Arrow").GetComponent<Image>().color = new Color(1, .9f, .7f, up ? 1 : .3f);
        _down.transform.Find("Arrow").GetComponent<Image>().color = new Color(1, .9f, .7f, down ? 1 : .3f);
    }

    [HideFromIl2Cpp]
    internal void SetVisible(bool visible)
    {
        if (_group == null) return;
        _group.alpha = visible ? 1 : 0;
        _group.blocksRaycasts = visible;
        // Keep selectable navigation available while hidden: focused cards reveal their arrows.
        _up.interactable = _canUp;
        _down.interactable = _canDown;
    }

    [HideFromIl2Cpp]
    internal void Hide() => SetVisible(false);
}
