// Managed stand-ins for native objects. Tests execute the production selection,
// view cache and patch callbacks, but do not claim to exercise IL2CPP detours.
namespace Il2CppSystem
{
    public delegate void Action();
    public delegate void Action<T>(T value);
}
namespace Il2CppSystem.Collections.Generic
{
    public sealed class List<T> : Nivalis.NativeObject
    {
        private readonly System.Collections.Generic.List<T> _items = new();
        public int _version, Reads;
        public int Count => _items.Count;
        public T this[int index] { get { Reads++; return _items[index]; } }
        public void Add(T item) { _items.Add(item); _version++; }
        public void Remove(T item) { if (_items.Remove(item)) _version++; }
        public void Clear() { _items.Clear(); _version++; }
        public bool Contains(T item) => _items.Contains(item);
    }
}
namespace UnityEngine
{
    public class MonoBehaviour(IntPtr pointer) { public IntPtr Pointer { get; } = pointer; public bool enabled = true; }
    public class GameObject
    {
        public T? GetComponent<T>() where T : class => null;
        public void SetActive(bool active) { }
    }
}
namespace UnityEngine.UI
{
    public static class LayoutRebuilder { public static void MarkLayoutForRebuild(object target) { } }
}
namespace Nivalis
{
    public class NativeObject
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
    }
    public sealed class Quest : NativeObject { }
    public sealed class RuntimeQuest
    {
        public Quest Quest = new();
        public bool IsActive = true, Pinned;
    }
    public sealed class SafeEvent
    {
        private readonly List<Il2CppSystem.Action> _listeners = new();
        public int Count => _listeners.Count;
        public void AddListener(Il2CppSystem.Action action) => _listeners.Add(action);
        public void RemoveListener(Il2CppSystem.Action action) => _listeners.Remove(action);
        public void Invoke() { foreach (var action in _listeners.ToArray()) action(); }
    }
    public sealed class SafeEvent<T>
    {
        private readonly List<Il2CppSystem.Action<T>> _listeners = new();
        public int Count => _listeners.Count;
        public void AddListener(Il2CppSystem.Action<T> action) => _listeners.Add(action);
        public void RemoveListener(Il2CppSystem.Action<T> action) => _listeners.Remove(action);
        public void Invoke(T value) { foreach (var action in _listeners.ToArray()) action(value); }
    }
    public sealed class QuestMap
    {
        public readonly List<RuntimeQuest> Entries = new();
        public int Reads;
        public List<RuntimeQuest> Values { get { Reads++; return Entries; } }
    }
    public sealed class QuestManager : NativeObject
    {
        public static QuestManager? _instance;
        public readonly QuestMap _activeQuests = new();
        public readonly SafeEvent OnQuestsPinnedChanged = new(), OnQuestsUpdated = new();
        public readonly SafeEvent<Quest> OnQuestStarted = new(), OnQuestCompleted = new();
        public void InitializeExternal() { }
        public void OnDestroyInternal() { }
    }
    public static class SerializationManager { public static void Clear() { } }
}
namespace Nivalis.Navigation
{
    public sealed class NavigationManager : Nivalis.NativeObject
    {
        public static NavigationManager? Instance;
        public Il2CppSystem.Collections.Generic.List<Nivalis.UI.CompassMarkerData> _markers = new();
        public void RegisterCompassMarker(Nivalis.UI.CompassMarkerData marker) => _markers.Add(marker);
        public void UnregisterCompassMarker(Nivalis.UI.CompassMarkerData marker) => _markers.Remove(marker);
    }
}
namespace Nivalis.UI
{
    public sealed class CompassMarkerData : Nivalis.NativeObject
    {
        public Nivalis.Quest? Quest;
        public int Position;
    }
    public sealed class NavigationUI { public void LateUpdate() { } }
    public sealed class ActiveJournalEntryItem { public Nivalis.RuntimeQuest? _quest = null; }
    public sealed class UiItem { public UnityEngine.GameObject GameObject = new(); }
    public sealed class UiList
    {
        public int DisplayedCount => 0;
        public UiItem GetItem(int index) => new();
    }
    public sealed class ActiveJournalEntriesUi
    {
        public bool _hidden;
        public int Hides, Shows;
        public void Start() { }
        public void OnToggleQuestHUDDIsplayPreformed() { _hidden = !_hidden; }
        public void HideAnimated() => Hides++;
        public void ShowAnimated() => Shows++;
        public UiList questList = new(), venueQuestList = new();
        public object entryParent = new();
        public void Refresh() { }
    }
}
namespace NivalisMods.HudOverhaul
{
    internal static class ModOptions
    {
        internal sealed class Toggle { internal bool Value = true; }
        internal static readonly Toggle Quests = new();
    }
    public sealed partial class Plugin
    {
        internal const string Id = "local.nivalis.hudoverhaul";
        internal sealed class TestLogger
        {
            public void LogError(object error) => throw new Exception(error.ToString());
            public void LogInfo(object message) { }
        }
        internal static readonly TestLogger Logger = new();
        internal static void Guard(string operation, Action action) => action();
    }
    internal static class Labels { internal static string Get(string key) => key; }
    internal static class CompanionSettings
    {
        internal static int AvailabilityUpdates;
        internal static void SetQuestAvailability() => AvailabilityUpdates++;
    }
}
namespace BepInEx.Unity.IL2CPP
{
    public sealed class IL2CPPChainloader
    {
        public sealed class PluginMetadata { public string Name = "Tracked Quests HUD"; }
        public sealed class LoadedPlugin { public PluginMetadata Metadata = new(); }
        public static readonly IL2CPPChainloader Instance = new();
        public readonly Dictionary<string, LoadedPlugin> Plugins = new();
        public event Action? Finished;
        public void Finish() => Finished?.Invoke();
    }
}

// The real Harmony metadata API is covered by QuestBackoffChecks. Here a
// recording patcher makes registration decisions observable without detouring
// the test host or loading BepInEx's native runtime outside the game.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(string method) { }
        public HarmonyPatch() { }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    public sealed class Patches(params string[] owners) { public string[] Owners => owners; }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type type, string method) => type.GetMethod(method,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)!;
        public static Type[] GetTypesFromAssembly(System.Reflection.Assembly assembly) => assembly.GetTypes();
    }
    public sealed class Harmony(string owner)
    {
        public static readonly List<Type> Processed = new();
        public static readonly Dictionary<Type, string> Installed = new();
        public static readonly Dictionary<System.Reflection.MethodBase, Patches> Metadata = new();
        public static int Inspections;
        public sealed class Processor(Type type, string owner)
        {
            public void Patch()
            {
                if (type.IsDefined(typeof(HarmonyPatch), false)) Installed[type] = owner;
            }
        }
        public Processor CreateClassProcessor(Type type) { Processed.Add(type); return new(type, owner); }
        public void UnpatchSelf()
        {
            foreach (var key in Installed.Where(p => p.Value == owner).Select(p => p.Key).ToArray()) Installed.Remove(key);
        }
        public static Patches? GetPatchInfo(System.Reflection.MethodBase method)
        { Inspections++; return Metadata.GetValueOrDefault(method); }
    }
}
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInDependency(string guid, BepInDependency.DependencyFlags flags) : Attribute
    {
        public string DependencyGUID => guid;
        public DependencyFlags Flags => flags;
        public enum DependencyFlags { SoftDependency }
    }
}
namespace NivalisMods.HudOverhaul
{
    internal sealed class NonQuestTarget { public void Update() { } }
    [HarmonyLib.HarmonyPatch(typeof(NonQuestTarget), nameof(NonQuestTarget.Update))]
    internal static class NonQuestSentinel { }
}
