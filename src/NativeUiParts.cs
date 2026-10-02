using Nivalis.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NivalisMods.HudOverhaul;

internal static class NativeUiParts
{
    internal static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name).AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    internal static void Image(Image? source, GameObject target)
    {
        if (source == null) return;
        var image = target.AddComponent<Image>();
        image.sprite = source.sprite; image.type = source.type;
        image.color = source.color; image.material = source.material;
        image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        image.raycastTarget = false;
    }
    internal static TMP_Text Text(Transform parent, TMP_Text source, string value)
    {
        var rect = Rect("Label", parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = source.font; text.fontSharedMaterial = source.fontSharedMaterial;
        text.fontSize = source.fontSize; text.color = source.color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false; text.text = value;
        return text;
    }
    internal static void Relabel(GameObject clone, string text)
    {
        foreach (var localized in clone.GetComponentsInChildren<LocalizedStaticUILabel>(true))
            Object.DestroyImmediate(localized);
        var label = clone.transform.Find("ButtonText")?.GetComponent<TMP_Text>()
            ?? clone.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text; label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize; label.fontSizeMin = 12;
            label.enableWordWrapping = false;
        }
        foreach (var name in new[] { "ButtonImageForGamepadHolder", "ButtonImageForGamepad", "GamepadButtonIcon", "Icon" })
            clone.transform.Find(name)?.gameObject.SetActive(false);
    }
}
