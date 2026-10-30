// Managed fixtures exercise the production callbacks, not IL2CPP detours.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
}
namespace Il2CppSystem.Collections.Generic
{
    public sealed class List<T> : System.Collections.Generic.List<T> { }
}
namespace Nivalis.GhostSystem.Ai
{
    public sealed class Item { public string Name = ""; }
    public sealed class VendorItem { public Item? ItemType; public int CurrentStock; }
    public sealed class Vendor
    {
        public Dictionary<Item, VendorItem>? items = new();
    }
    public sealed class Person
    {
        public string Name = "";
        public int Location, Type;
        public bool FailLookup;
        public Vendor? Vendor;
        public Vendor? VendorDefinition => FailLookup ? throw new InvalidOperationException("Lookup failed") : Vendor;
    }
}
namespace Nivalis.Locale.UI
{
    public sealed class Input { public string m_Text = ""; public string text => m_Text; }
    public sealed class VendorFilteringUI
    {
        public Input searchField = new();
        public int Location, Type;
        public static bool TextMatches(string text, string searchText) => text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        public string? GetProductsFilter(Nivalis.GhostSystem.Ai.Person person) => TextMatches(person.Name, searchField.text) ? null : searchField.text;
        public void FilterResult(Il2CppSystem.Collections.Generic.List<Nivalis.GhostSystem.Ai.Person> persons) =>
            persons.RemoveAll(p => !p.Name.Contains(searchField.text, StringComparison.OrdinalIgnoreCase)
                || (Location != 0 && p.Location != Location) || (Type != 0 && p.Type != Type));
    }
}
namespace NivalisMods.HudOverhaul
{
    internal static class ModOptions
    {
        internal sealed class Toggle { internal bool Value = true; }
        internal static readonly Toggle Search = new();
    }
    internal static class Plugin
    {
        internal sealed class Log { internal int Errors; internal void LogError(string message) => Errors++; }
        internal static readonly Log Logger = new();
        internal static void Guard(string label, Action action) => action();
    }
}
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public static Vector2 zero => default; }
    public sealed class CanvasGroup { public float alpha = 1; }
    public static class Resources
    {
        public static object[] Objects = [];
        public static T[] FindObjectsOfTypeAll<T>() => Objects.OfType<T>().ToArray();
    }
}
namespace Nivalis.UI.InGameMenu.CharacterWindow
{
    public sealed class ScrollRect { public UnityEngine.Vector2 velocity; public float verticalNormalizedPosition; }
    public sealed class Toggle { public bool isOn = true; }
    public sealed class CharacterWindowUi
    {
        public bool isActiveAndEnabled = true;
        public Toggle vendorCharactersToggle = new();
        public Nivalis.Locale.UI.VendorFilteringUI vendorFilteringUI = new();
        public VendorDetails vendorDetails = new();
        public ScrollRect vendorProductsListScrollRect = new();
        public void RefreshCharacterList() { }
    }
    public sealed class VendorDetails
    {
        public CharacterWindowUi? Parent;
        public Nivalis.GhostSystem.Ai.Person? _person;
        public UnityEngine.CanvasGroup canvasGroup = new();
        public int Initializations;
        public string? LastFilter;
        public T? GetComponentInParent<T>(bool includeInactive) where T : class => Parent as T;
        public void Initialize(Nivalis.GhostSystem.Ai.Person person, string? productsFilter = null)
        {
            _person = person; LastFilter = productsFilter; Initializations++;
        }
    }
}
