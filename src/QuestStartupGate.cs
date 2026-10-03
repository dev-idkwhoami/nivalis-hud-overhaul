namespace NivalisMods.HudOverhaul;

// BepInEx signals readiness; the next Unity update consumes it exactly once.
// Do not retry a blocked or failed registration while the game is running.
internal sealed class QuestStartupGate
{
    private bool _ready, _completed;
    internal void PluginsLoaded() => _ready = true;
    internal bool TryBegin()
    {
        if (!_ready || _completed) return false;
        _completed = true;
        return true;
    }
}
