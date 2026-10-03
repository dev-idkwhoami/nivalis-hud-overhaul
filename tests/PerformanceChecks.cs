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

        using var work = new FrameWork();
        var visited = new List<int>();
        var disposed = 0;
        IEnumerable<bool> Scan(int count, bool fail = false)
        {
            try
            {
                for (var i = 0; i < count; i++)
                {
                    if (fail && i == 2) throw new InvalidOperationException("scan failure");
                    visited.Add(i);
                    yield return true;
                }
            }
            finally { disposed++; }
        }
        work.Start(Scan(100));
        work.Tick(8, double.PositiveInfinity);
        check("large history scan is bounded per frame", visited.Count == 8 && work.Pending);
        while (work.Pending) work.Tick(8, double.PositiveInfinity);
        check("split scan visits every item in order exactly once", visited.SequenceEqual(Enumerable.Range(0, 100)) && disposed == 1);
        visited.Clear();
        work.Start(Scan(100));
        work.Tick(8, 0);
        check("elapsed budget yields after an indivisible step", visited.Count == 1 && work.Pending);
        work.Dispose();
        work.Tick();
        check("unload cancellation discards remaining work", visited.Count == 1 && disposed == 2 && !work.Pending);
        visited.Clear();
        work.Start(Scan(100)); work.Tick(1, double.PositiveInfinity);
        work.Start(Scan(2));
        while (work.Pending) work.Tick();
        check("new scan cannot continue work from previous save", visited.SequenceEqual(new[] { 0, 0, 1 }) && disposed == 4);
        work.Start(Scan(100, fail: true));
        var threw = false;
        try { work.Tick(8, double.PositiveInfinity); }
        catch (InvalidOperationException) { threw = true; }
        check("failed scan is disposed rather than retried every frame", threw && !work.Pending && disposed == 5);
    }
}
