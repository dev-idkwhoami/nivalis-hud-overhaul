// Managed fixtures exercise the production callbacks, not IL2CPP detours.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
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
    }
}
