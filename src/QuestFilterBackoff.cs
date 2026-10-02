using System.Reflection;
using HarmonyLib;

namespace NivalisMods.HudOverhaul;

// Harmony can identify overlapping hooks, but cannot tell what another patch does.
// Yield conservatively to any other owner on this specific display method.
internal sealed class QuestFilterBackoff(MethodBase target, string ownId, Action<string> log,
    Func<MethodBase, Patches?>? inspect = null)
{
    private string _lastReason = "";
    private readonly Func<MethodBase, Patches?> _inspect = inspect ?? Harmony.GetPatchInfo;
    internal string[] OtherOwners { get; private set; } = Array.Empty<string>();

    internal bool ShouldBackOff()
    {
        string reason;
        try
        {
            var patches = _inspect(target);
            OtherOwners = patches == null ? Array.Empty<string>() : patches.Owners
                .Where(owner => !string.Equals(owner, ownId, StringComparison.Ordinal))
                .OrderBy(owner => owner, StringComparer.Ordinal).ToArray();
            reason = string.Join(", ", OtherOwners);
        }
        catch (Exception e)
        {
            // Do not touch shared display state when ownership cannot be checked.
            OtherOwners = Array.Empty<string>();
            reason = "patch inspection failed: " + e.GetType().Name;
        }

        if (reason != _lastReason)
        {
            _lastReason = reason;
            log(reason.Length > 0
                ? $"Quest filtering: backing off {target.DeclaringType?.Name}.{target.Name}; {reason}."
                : $"Quest filtering: resuming {target.DeclaringType?.Name}.{target.Name}; no other patch owners remain.");
        }
        return reason.Length > 0;
    }
}
