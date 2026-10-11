// Managed fixtures model the updated API; native calls still need an in-game test.
namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
}
namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T>
    {
        public TCast Cast<TCast>() => (TCast)(object)this;
    }
    public class Dictionary<K, V> : System.Collections.Generic.Dictionary<K, V> where K : notnull { }
    public interface IReadOnlyCollection<T> : System.Collections.Generic.IReadOnlyCollection<T> { }
}
namespace NivalisMods.ModCompanion.Api
{
    public sealed record Choice<T>(T Value, string Label);
    public sealed class Setting<T>
    {
        public T Value = default!;
        public event Action<T>? Changed { add { } remove { } }
    }
    public sealed class SettingsCategory
    {
        public Setting<T> Choice<T>(string key, string label, T value, Choice<T>[] choices, string hint) => new() { Value = value };
    }
}
namespace Nivalis.InventorySystem
{
    public sealed class ItemType { public string Guid = "meal"; }
    public sealed class Inventory
    {
        public readonly Dictionary<ItemType, int> Stock = new();
        public Inventory Items => this;
        public Inventory itemContainer => this;
        public int GetItemCount(ItemType item) => Stock.GetValueOrDefault(item);
        public int GetItemCount(ItemType item, out int ignored) { ignored = 0; return GetItemCount(item); }
    }
}
namespace Nivalis.GhostSystem.CustomerLoop
{
    using Nivalis.InventorySystem;
    public sealed class Venue { public RuntimeData RuntimeData = new(); }
    public sealed class RuntimeData
    {
        public Inventory InventoryData = new();
        public System.Collections.Generic.List<MenuItem> Menu = new();
        public ReceiptLookup Receipts = new();
        public int Target;
        public int GetSupplyTarget(ItemType item) => Target;
    }
    public sealed class MenuItem
    {
        public Recipe Recipe = new();
        public Recipe? VenueRecipe;
        public Venue? ResolvedVenue;
        public Recipe GetRecipe(Venue venue) { ResolvedVenue = venue; return VenueRecipe ?? Recipe; }
    }
    public sealed class Recipe
    {
        public Output Output = new();
        public Input[] Inputs = [];
    }
    public sealed class Output { public ItemType type = new(); public int amount = 1; }
    public sealed class Input { public ItemType DefaultItem = new(); public int Amount = 1; }
    public sealed class RestaurantReceipt { public ItemType Meal = new(); public GameTime Time = new(); public int Count; }
    public sealed class ReceiptList<T> : Il2CppSystem.Collections.Generic.List<T>, Il2CppSystem.Collections.Generic.IReadOnlyCollection<T> { }
    public sealed class ReceiptLookup
    {
        public ReceiptLookup ReceiptsLookup => this;
        public ReceiptList<RestaurantReceipt> History = new();
        public int Reads;
        public ReceiptList<T> GetReceiptsOfType<T>() { Reads++; return (ReceiptList<T>)(object)History; }
    }
}
namespace Nivalis
{
    using Nivalis.InventorySystem;
    using Nivalis.GhostSystem.CustomerLoop;
    using Il2CppSystem.Collections.Generic;
    public sealed class GameTime { public int GameplayGameDay = 8; }
    public static class TimeOfDayManager { public static GameTime CurrentTime = new(); }
    public sealed class ShoppingListManager
    {
        public static readonly ShoppingListManager Instance = new();
        public readonly struct ShoppingListData(int amount, int demand)
        {
            public readonly int amount = amount, demand = demand;
            public int MissingItems => Math.Max(0, demand - amount);
        }
        public sealed class VenueLowIngredientsData { public Dictionary<ItemType, ShoppingListData> LowIngredients = new(); }
        public Dictionary<Venue, VenueLowIngredientsData> VenuesLowOnIngredientsMap = new();
        public Dictionary<ItemType, ShoppingListData> _ShoppingList_k__BackingField = new();
        public Dictionary<ItemType, ShoppingListData> ShoppingList => _ShoppingList_k__BackingField;
    }
}
namespace Nivalis.Player
{
    using Nivalis.GhostSystem.CustomerLoop;
    using Nivalis.InventorySystem;
    public sealed class PlayerManager
    {
        public static readonly PlayerManager Instance = new();
        public Player LocalPlayer = new();
    }
    public sealed class Player
    {
        public Inventory Inventory = new();
        public List<Venue> Venues = new();
        public void GetOwnedVenues(Il2CppSystem.Collections.Generic.List<Venue> result) => result.AddRange(Venues);
    }
}
namespace Nivalis.UI
{
    public sealed class ShoppingListPanel
    {
        public ShoppingListPanel gameObject => this;
        public bool activeInHierarchy => false;
        public void RefreshItemsList() { }
        public void ScrollToTop() { }
    }
}
namespace UnityEngine
{
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() => []; }
}
namespace NivalisMods.HudOverhaul
{
    using NivalisMods.ModCompanion.Api;
    internal static class ModOptions
    {
        internal static readonly Setting<bool> DeductVenueStock = new();
        internal static readonly Setting<int> StockDays = new() { Value = 1 };
    }
    internal static class Plugin
    {
        internal static readonly Log Logger = new();
        internal static void Guard(string operation, Action action) => action();
    }
    internal sealed class Log { internal void LogWarning(string message) { } }
}
