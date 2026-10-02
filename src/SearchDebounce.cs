using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Nivalis.Locale.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace NivalisMods.HudOverhaul;

public sealed class SearchDebounce : MonoBehaviour
{
    private TMP_InputField _input = null!;
    private TMP_InputField.OnChangeEvent _original = null!;
    private readonly SearchDelay _delay = new();
    public SearchDebounce(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal static void Attach(InventoryItemFilteringUi filter)
    {
        var input = filter.searchField;
        if (input == null || input.GetComponent<SearchDebounce>() != null) return;
        var debounce = input.gameObject.AddComponent<SearchDebounce>();
        debounce._input = input;
        debounce._original = input.onValueChanged;
        var changed = new TMP_InputField.OnChangeEvent();
        changed.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>(
            new Action<string>(value =>
            {
                if (ModOptions.DebounceMs.Value == 0) { debounce._delay.Cancel(); debounce._original.Invoke(value); }
                else debounce._delay.Queue(Time.unscaledTime, ModOptions.DebounceMs.Value / 1000.0);
            })));
        input.onValueChanged = changed;
    }

    public void Update()
    {
        if (_input == null || !_delay.Take(Time.unscaledTime)) return;
        Plugin.Guard("Debounced search refresh", () => _original.Invoke(_input.text));
    }

    public void OnDisable() => _delay.Cancel();
}
