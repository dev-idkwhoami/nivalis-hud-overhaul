using Nivalis.UI;
using NivalisMods.HudOverhaul;

var hud = new ActiveJournalEntriesUi();
UnityEngine.Object.Instances = [hud];
ModOptions.QuestHudVisible.Value = false;
QuestHudVisibility.ApplySetting();
Check(hud._hidden && hud.HideCalls == 1, "saved preference hides HUD without a quest-filter module");
QuestHudVisibility.ApplySetting();
Check(hud.HideCalls == 1, "unchanged preference does not restart animation");
ModOptions.QuestHudVisible.Value = true;
QuestHudVisibility.ApplySetting();
Check(!hud._hidden && hud.ShowCalls == 1, "settings toggle restores HUD");
hud._hidden = true;
QuestHudVisibility.Remember(hud);
Check(!ModOptions.QuestHudVisible.Value, "native hide toggle persists");
var replacement = new ActiveJournalEntriesUi();
QuestHudVisibility.Restore(replacement);
Check(replacement._hidden, "replacement HUD restores saved visibility");
replacement._hidden = false;
QuestHudVisibility.Remember(replacement);
Check(ModOptions.QuestHudVisible.Value, "native show toggle persists");
replacement.HideAnimated();
Check(ModOptions.QuestHudVisible.Value, "temporary animation does not change saved preference");
UnityEngine.Object.Instances = [];
QuestHudVisibility.ApplySetting();
Console.WriteLine("Passed 8 quest HUD visibility checks, including no active HUD.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

namespace NivalisMods.HudOverhaul
{
    internal sealed class Setting { internal bool Value; }
    internal static class ModOptions { internal static readonly Setting QuestHudVisible = new(); }
    internal static class Plugin { internal static void Guard(string operation, Action action) => action(); }
}

namespace Nivalis.UI
{
    public sealed class ActiveJournalEntriesUi
    {
        public bool _hidden;
        public int HideCalls, ShowCalls;
        public void HideAnimated() => HideCalls++;
        public void ShowAnimated() => ShowCalls++;
        public void Start() { }
        public void OnToggleQuestHUDDIsplayPreformed() { }
    }
}

namespace UnityEngine
{
    public static class Object
    {
        public static object[] Instances = [];
        public static T[] FindObjectsOfType<T>() => Instances.OfType<T>().ToArray();
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(string method) { }
    }
    public sealed class HarmonyPostfix : Attribute { }
}
