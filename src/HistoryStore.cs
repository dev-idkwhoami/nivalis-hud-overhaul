using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NivalisMods.HudOverhaul;

// One worker owns the file and its indexes. Each checksummed frame commits a whole
// batch. Only an incomplete final frame is discarded after an interrupted write.
internal sealed class HistoryStore : IDisposable
{
    private const string Magic = "HUDLOG01";
    private const int MaxFrame = 64 * 1024 * 1024;
    private readonly FileStream _file;
    private readonly Dictionary<long, (long? Parent, long Until)> _segments = new();
    private readonly Dictionary<string, (long Segment, long Until)> _checkpoints = new();
    private readonly Dictionary<string, List<(long Id, long Segment, HistoryEvent Event)>> _events = new();
    private long _lastId;
    internal long Segment { get; private set; }

    // Public properties are required by System.Text.Json; this is our on-disk schema.
    internal sealed class Frame
    {
        public string Type { get; set; } = "";
        public long Segment { get; set; }
        public long? Parent { get; set; }
        public long Until { get; set; }
        public string Hash { get; set; } = "";
        public string Name { get; set; } = "";
        public long FirstId { get; set; }
        public HistoryEvent[] Items { get; set; } = Array.Empty<HistoryEvent>();
    }

    internal HistoryStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            if (_file.Length == 0)
            {
                _file.Write(Encoding.ASCII.GetBytes(Magic));
                _file.Flush(true);
            }
            _file.Position = 0;
            using var reader = new BinaryReader(_file, Encoding.UTF8, leaveOpen: true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != Magic)
                throw new InvalidDataException("Unknown history log format.");
            while (_file.Position < _file.Length)
            {
                var start = _file.Position;
                if (_file.Length - start < 36) { RecoverTail(start); break; }
                var length = reader.ReadInt32();
                if (length <= 0 || length > MaxFrame) throw new InvalidDataException("Invalid history frame length.");
                var checksum = reader.ReadBytes(32);
                if (_file.Length - _file.Position < length) { RecoverTail(start); break; }
                var bytes = reader.ReadBytes(length);
                if (!SHA256.HashData(bytes).SequenceEqual(checksum))
                    throw new InvalidDataException("History checksum failed; file left intact.");
                var frame = JsonSerializer.Deserialize<Frame>(bytes) ?? throw new InvalidDataException("Empty history frame.");
                Validate(frame);
                Apply(frame);
            }
        }
        catch { _file.Dispose(); throw; }
    }

    private void RecoverTail(long position) { _file.SetLength(position); _file.Position = position; _file.Flush(true); }

    internal bool Fork(string hash)
    {
        var known = _checkpoints.TryGetValue(hash, out var saved);
        var id = _segments.Count == 0 ? 1 : _segments.Keys.Max() + 1;
        Commit(new Frame { Type = "segment", Segment = id, Parent = known ? saved.Segment : null, Until = saved.Until });
        Segment = id;
        return known;
    }

    internal void Append(IEnumerable<HistoryEvent> events)
    {
        var items = events.ToArray();
        if (items.Length == 0) return;
        Commit(new Frame { Type = "events", Segment = Segment, FirstId = checked(_lastId + 1), Items = items });
    }

    internal void Checkpoint(string hash, string name) => Commit(new Frame
    { Type = "checkpoint", Segment = Segment, Hash = hash, Name = name, Until = _lastId });

    private void Commit(Frame frame)
    {
        Validate(frame);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        if (bytes.Length > MaxFrame) throw new InvalidDataException("History batch is too large.");
        using var writer = new BinaryWriter(_file, Encoding.UTF8, leaveOpen: true);
        writer.Write(bytes.Length);
        writer.Write(SHA256.HashData(bytes));
        writer.Write(bytes);
        writer.Flush();
        _file.Flush(true);
        Apply(frame);
    }

    private void Validate(Frame f)
    {
        if (f.Type == "segment")
        {
            if (f.Segment <= 0 || _segments.ContainsKey(f.Segment) ||
                (f.Parent.HasValue && (!_segments.ContainsKey(f.Parent.Value) || f.Parent.Value >= f.Segment)) || f.Until < 0)
                throw new InvalidDataException("Invalid history branch.");
            return;
        }
        if (!_segments.ContainsKey(f.Segment)) throw new InvalidDataException("Missing history branch.");
        if (f.Type == "checkpoint")
        {
            if (f.Hash == null || f.Name == null || f.Until < 0 || f.Until > _lastId)
                throw new InvalidDataException("Invalid history checkpoint.");
        }
        else if (f.Type == "events")
        {
            if (f.FirstId <= _lastId || f.Items == null || f.Items.Length == 0 || f.FirstId > long.MaxValue - f.Items.Length)
                throw new InvalidDataException("Invalid history batch.");
            foreach (var e in f.Items)
                if (e == null || e.Kind == null || e.Venue == null || e.Subject == null || e.Name == null || e.Group == null || e.Source == null)
                    throw new InvalidDataException("Invalid history event.");
        }
        else throw new InvalidDataException("Unknown history record type.");
    }

    private void Apply(Frame f)
    {
        if (f.Type == "segment") _segments.Add(f.Segment, (f.Parent, f.Until));
        else if (f.Type == "checkpoint") _checkpoints[f.Hash] = (f.Segment, f.Until);
        else
        {
            var id = f.FirstId;
            foreach (var e in f.Items)
            {
                if (!_events.TryGetValue(e.Kind, out var list)) _events[e.Kind] = list = new();
                list.Add((id++, f.Segment, e));
            }
            _lastId = id - 1;
        }
    }

    internal List<HistoryEvent> Read(string kind, string? venue = null, int from = 0, int to = int.MaxValue)
    {
        var lineage = new Dictionary<long, long>();
        long? segment = Segment;
        var until = long.MaxValue;
        while (segment.HasValue && _segments.TryGetValue(segment.Value, out var parent))
        {
            lineage.Add(segment.Value, until);
            segment = parent.Parent;
            until = Math.Min(until, parent.Until);
        }
        if (!_events.TryGetValue(kind, out var events)) return new();
        return events.Where(row => lineage.TryGetValue(row.Segment, out var limit) && row.Id <= limit &&
            (venue == null || row.Event.Venue == venue) && row.Event.Day >= from && row.Event.Day < to)
            .Select(row => row.Event).ToList();
    }

    public void Dispose() => _file.Dispose();
}
