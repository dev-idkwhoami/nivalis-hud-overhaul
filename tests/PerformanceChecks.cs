using NivalisMods.HudOverhaul;

internal static class PerformanceChecks
{
    internal static void Run(Action<string, bool> check)
    {
        var pins = new QuestPinFilter([new(1, true, true), new(2, true, true)]);
        pins.Clear();
        pins.Add(new(1, false, true));
        pins.Add(new(2, true, false));
        check("reusing quest storage removes completed and unpinned quests", !pins.HasPins && pins.ShowCompassMarker(1));
        pins.Add(new(3, true, true));
        check("new save pins replace previous selection", pins.ShowQuest(3) && !pins.ShowQuest(1) && pins.ShowCompassMarker(0));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            pins.Clear();
            pins.Add(new(3, true, true));
            pins.Add(new(4, true, true));
        }
        check("steady quest set refresh allocates no managed garbage", GC.GetAllocatedBytesForCurrentThread() == before);
    }
}
