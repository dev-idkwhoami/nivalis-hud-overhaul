using Il2CppInterop.Runtime;
using Nivalis.Locale.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NivalisMods.HudOverhaul;

// Fresh Toggle components reuse native tab art without copying settings/review
// callbacks or BetterUI state machines into an unrelated window.
internal sealed class ShoppingTabs
{
    private readonly Toggle _standard;
    private readonly Toggle _estimated;
    internal ShoppingTabs(TMP_Dropdown venue, ScrollRect scroll)
    {
        var wrapper = venue.transform.parent;
        var root = new GameObject("HUDOverhaul.ShoppingTabs");
        var rect = root.AddComponent<RectTransform>();
        rect.SetParent(wrapper, false);
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = new Vector2(-24, 36);
        rect.anchoredPosition = new Vector2(0, -4);
        var group = root.AddComponent<ToggleGroup>();
        group.allowSwitchOff = false;
        Image? template = null;
        foreach (var reviews in Resources.FindObjectsOfTypeAll<LocaleReviewOverviewTab>())
        {
            template = reviews.toggleToday?.targetGraphic?.TryCast<Image>();
            if (template != null) break;
        }
        _standard = Create(rect, group, venue.captionText, template, false);
        _estimated = Create(rect, group, venue.captionText, template, true);
        venue.GetComponent<RectTransform>().anchoredPosition += new Vector2(0, -42);
        scroll.GetComponent<RectTransform>().offsetMax += new Vector2(0, -42);
        Sync();
    }

    private static Toggle Create(RectTransform parent, ToggleGroup group, TMP_Text font, Image? template, bool estimated)
    {
        var root = new GameObject(estimated ? "Estimated" : "Standard");
        root.SetActive(false);
        var rect = root.AddComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(estimated ? .5f : 0, 0);
        rect.anchorMax = new Vector2(estimated ? 1 : .5f, 1);
        rect.offsetMin = new Vector2(estimated ? 3 : 0, 0);
        rect.offsetMax = new Vector2(estimated ? 0 : -3, 0);
        var background = root.AddComponent<Image>();
        if (template != null) { background.sprite = template.sprite; background.type = template.type; }
        background.color = new Color(.29f, .20f, .12f, 1);
        var activeObject = new GameObject("Selected");
        var activeRect = activeObject.AddComponent<RectTransform>(); activeRect.SetParent(rect, false);
        activeRect.anchorMin = Vector2.zero; activeRect.anchorMax = Vector2.one;
        activeRect.offsetMin = activeRect.offsetMax = Vector2.zero;
        var active = activeObject.AddComponent<Image>();
        active.sprite = background.sprite; active.type = background.type;
        active.color = new Color(.67f, .49f, .27f, 1); active.raycastTarget = false;
        var labelObject = new GameObject("Label");
        var labelRect = labelObject.AddComponent<RectTransform>(); labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8, 0); labelRect.offsetMax = new Vector2(-8, 0);
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.font = font.font; label.fontSize = font.fontSize; label.enableAutoSizing = true;
        label.fontSizeMax = font.fontSize; label.fontSizeMin = 12;
        label.alignment = TextAlignmentOptions.Center; label.enableWordWrapping = false;
        label.color = new Color(1, .94f, .82f, 1); label.raycastTarget = false;
        label.text = Labels.Get(estimated ? "shopping.estimated" : "shopping.standard");
        var toggle = root.AddComponent<Toggle>();
        toggle.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        toggle.targetGraphic = background; toggle.graphic = active;
        toggle.SetIsOnWithoutNotify(estimated == EstimatedShopping.Enabled);
        toggle.group = group;
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(on =>
        {
            if (on) Plugin.Guard("Switch shopping mode", () => EstimatedShopping.Select(estimated));
        })));
        root.SetActive(true);
        return toggle;
    }
    internal void Sync()
    {
        _standard.SetIsOnWithoutNotify(!EstimatedShopping.Enabled);
        _estimated.SetIsOnWithoutNotify(EstimatedShopping.Enabled);
    }
}
