// Managed model of native data/binder ownership, not an IL2CPP runtime test.
namespace Il2CppSystem
{
    public class Object { public T Cast<T>() => (T)(object)this; public T? TryCast<T>() where T : class => this as T; }
    public sealed class Type(System.Type value) { public System.Type Value = value; }
    public sealed class Action<A,B,C,D> : Object
    {
        public object? Target;
        public System.Reflection.MethodInfo Method = null!;
        public void Invoke(A a, B b, C c, D d) => Method.Invoke(Target, [a,b,c,d]);
    }
    public static class Delegate
    {
        public static Object CreateDelegate(Type type, Object owner, string name) => Create(type, owner.GetType(), owner, name);
        public static Object CreateDelegate(Type type, Type owner, string name) => Create(type, owner.Value, null, name);
        private static Object Create(Type type, System.Type ownerType, object? owner, string name)
        {
            var result = (Object)Activator.CreateInstance(type.Value)!;
            type.Value.GetField("Target")!.SetValue(result, owner);
            var method = ownerType.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)!;
            if (method == null) throw new Exception("Missing binder " + name);
            type.Value.GetField("Method")!.SetValue(result, method);
            return result;
        }
    }
    public sealed class ValueTuple<A,B>(A a, B b) : Object { public A Item1 = a; public B Item2 = b; }
}
namespace Il2CppSystem.Collections.Generic
{
    public interface IReadOnlyList<T> : System.Collections.Generic.IReadOnlyList<T> { }
    public sealed class List<T> : Il2CppSystem.Object, IReadOnlyList<T>
    {
        private readonly System.Collections.Generic.List<T> _items = new();
        public bool RejectManagedWrites;
        private void CheckWrite()
        {
            if (RejectManagedWrites) throw new InvalidOperationException("Native entries must not be written back from managed code.");
        }
        public T this[int i] { get => _items[i]; set { CheckWrite(); _items[i] = value; } }
        public int Count => _items.Count;
        public void Add(T item) { CheckWrite(); _items.Add(item); }
        public void Clear() { CheckWrite(); _items.Clear(); }
        public void Reverse(int index, int count) => _items.Reverse(index, count);
        public void RemoveRange(int index, int count) => _items.RemoveRange(index, count);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Il2CppInterop.Runtime
{
    public static class Il2CppType { public static Il2CppSystem.Type Of<T>() => new(typeof(T)); }
}
namespace Nivalis
{
    public sealed class RuntimeQuest : Il2CppSystem.Object { public int LastUpdateTime; public bool IsActive; }
}
namespace Nivalis.CraftingSystem
{
    public class IRecipe : Il2CppSystem.Object
    {
        public int Calories, Exclusivity;
        public void CalculateTags(out int calories, out int exclusivity) { calories = Calories; exclusivity = Exclusivity; }
    }
}
namespace Nivalis.GhostSystem.CustomerLoop
{
    public sealed class Venue : Il2CppSystem.Object { }
    public sealed class Review : Il2CppSystem.Object
    {
        public TimeStamp ReviewTime = new(); public int Score; public bool Positive;
    }
    public sealed class TimeStamp { public int TotalGameSeconds; }
}
namespace Nivalis.Locale
{
    public sealed class MealMenuItem : Il2CppSystem.Object
    {
        public CraftingSystem.IRecipe Customized = new();
        public GhostSystem.CustomerLoop.Venue? RequestedVenue;
        public CraftingSystem.IRecipe GetRecipe(GhostSystem.CustomerLoop.Venue venue) { RequestedVenue = venue; return Customized; }
    }
}
namespace Nivalis.UI
{
    public sealed class ItemListUI
    {
        public sealed class __c__DisplayClass101_0<TData,TView> : Il2CppSystem.Object where TView : class
        {
            public Il2CppSystem.Action<TView,TData,int,bool> bind = null!;
        }
        public sealed class Binder { public Il2CppSystem.Object? Target; }
        public Binder? _binder;
        public Il2CppSystem.Object? OriginalCallback, CurrentCallback;
        public void Seed<TData,TView>(Il2CppSystem.Collections.Generic.List<TData> data, Owner? owner, string method, System.Type? staticOwner = null) where TView : class
        {
            var type = new Il2CppSystem.Type(typeof(Il2CppSystem.Action<TView,TData,int,bool>));
            var callback = (owner != null ? Il2CppSystem.Delegate.CreateDelegate(type, owner, method)
                : Il2CppSystem.Delegate.CreateDelegate(type, new Il2CppSystem.Type(staticOwner!), method))
                .Cast<Il2CppSystem.Action<TView,TData,int,bool>>();
            data.RejectManagedWrites = true;
            OriginalCallback = callback;
            SetItems(data, callback, SelectedIndex);
        }
        public int SelectedIndex = -1;
        public int Rebinds;
        public object[] Data = [];
        public System.Action<int>? BindAt;
        // No physical row pool exists in this fixture. Ordering must use data.
        public void SetItems<TData,TView>(Il2CppSystem.Collections.Generic.IReadOnlyList<TData> data,
            Il2CppSystem.Action<TView,TData,int,bool> bind, int selected) where TView : class
        {
            CurrentCallback = bind;
            _binder = new Binder { Target = new __c__DisplayClass101_0<TData,TView> { bind = bind } };
            Data = data.Select(x => (object)x!).ToArray(); SelectedIndex = selected; Rebinds++;
            BindAt = i => bind.Invoke(Activator.CreateInstance<TView>(), (TData)Data[i], i, i == SelectedIndex);
        }
    }
    public class Owner : Il2CppSystem.Object
    {
        public object? Bound; public int BoundIndex; public bool BoundSelected;
        protected void Record(object data, int index, bool selected) { Bound = data; BoundIndex = index; BoundSelected = selected; }
    }
    public sealed class JournalListItemUI { }
    public sealed class ShopItemDisplayUI { }
    public sealed class JournalUI : Owner
    {
        public ItemListUI entriesListUi = new();
        public Il2CppSystem.Collections.Generic.List<RuntimeQuest> _quests = new();
        public void BindQuest(JournalListItemUI view, RuntimeQuest data, int index, bool selected) => Record(data,index,selected);
    }
    public sealed class ShopUiVendorPanel : Owner
    {
        public sealed class ItemType { public int Comfort; }
        public sealed class ItemStack { public ItemType Type = new(); }
        public sealed class ShopEntry { public ItemStack Item = new(); public string Identity = ""; }
        public ItemListUI itemsList = new();
        public Il2CppSystem.Collections.Generic.List<ShopEntry> _entries = new();
        public void BindEntry(ShopItemDisplayUI view, ShopEntry data, int index, bool selected) => Record(data,index,selected);
    }
}
namespace Nivalis.Locale.UI
{
    using Nivalis.UI;
    using Nivalis.GhostSystem.CustomerLoop;
    using Nivalis.CraftingSystem;
    public sealed class RecipeItemDisplayUI { }
    public sealed class MenuItemDisplayUI { public enum WarningType { None, Equipment } }
    public sealed class ReviewItemDisplayUi { }
    public sealed class MenuModificationWindow : Owner
    {
        public ItemListUI recipeItemList = new(), menuItemList = new();
        public Venue _venue = new();
        public Il2CppSystem.Collections.Generic.List<IRecipe> _listedRecipes = new();
        public Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<MealMenuItem,MenuItemDisplayUI.WarningType>> _listedMenuItems = new();
        public void BindRecipe(RecipeItemDisplayUI view, IRecipe data, int index, bool selected) => Record(data,index,selected);
        public void BindMenuItem(MenuItemDisplayUI view, Il2CppSystem.ValueTuple<MealMenuItem,MenuItemDisplayUI.WarningType> data, int index, bool selected) => Record(data,index,selected);
    }
    public sealed class LocaleReviewOverviewTab : Owner
    {
        public ItemListUI reviewList = new();
        public Il2CppSystem.Collections.Generic.List<Review> _listedReviews = new();
        public static Review? LastReview;
        public static void BindReview(ReviewItemDisplayUi view, Review data, int index, bool selected) => LastReview = data;
    }
}
