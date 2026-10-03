using System.Diagnostics;

namespace NivalisMods.HudOverhaul;

// Cooperative main-thread work: each yielded step is indivisible. Never move
// Unity/IL2CPP reads to the history file worker or retain native enumerators
// across frames. The time limit is checked between steps, not during one.
internal sealed class FrameWork : IDisposable
{
    private IEnumerator<bool>? _steps;
    internal bool Pending => _steps != null;

    internal void Start(IEnumerable<bool> steps)
    {
        Dispose();
        _steps = steps.GetEnumerator();
    }

    internal void Tick(int maximumSteps = 8, double milliseconds = 0.5)
    {
        if (_steps == null) return;
        var started = Stopwatch.GetTimestamp();
        try
        {
            for (var i = 0; i < maximumSteps; i++)
            {
                if (!_steps.MoveNext()) { Dispose(); return; }
                if ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency >= milliseconds) return;
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        var steps = _steps;
        _steps = null;
        steps?.Dispose();
    }
}
