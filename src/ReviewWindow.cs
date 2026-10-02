namespace NivalisMods.HudOverhaul;

// Pure geometry used by the review row pool; unmeasured rows use an estimate.
internal sealed class ReviewWindow
{
    private float[] _starts = [0];
    internal int Count => _starts.Length - 1;
    internal float Height => _starts[^1];
    internal float Top(int index) => _starts[index];
    internal void Reset(IEnumerable<float> heights, float spacing)
    {
        var values = heights.ToArray();
        _starts = new float[values.Length + 1];
        for (var i = 0; i < values.Length; i++)
            _starts[i + 1] = _starts[i] + Math.Max(1, values[i]) + (i + 1 < values.Length ? spacing : 0);
    }
    internal int At(float offset)
    {
        if (Count == 0) return 0;
        var found = Array.BinarySearch(_starts, Math.Max(0, offset));
        return Math.Clamp(found >= 0 ? found : ~found - 1, 0, Count - 1);
    }
    internal (int First, int Last) Visible(float offset, float viewport, int buffer = 2)
    {
        if (Count == 0) return (0, -1);
        return (Math.Max(0, At(offset) - buffer), Math.Min(Count - 1, At(offset + viewport) + buffer));
    }
}
