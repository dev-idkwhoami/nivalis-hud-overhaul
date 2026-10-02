namespace NivalisMods.HudOverhaul;

internal sealed class SearchDelay
{
    private double? _due;
    internal void Queue(double now, double seconds = 0.25) => _due = now + seconds;
    internal void Cancel() => _due = null;
    internal bool Take(double now)
    {
        if (_due == null || now < _due.Value) return false;
        _due = null;
        return true;
    }
}
