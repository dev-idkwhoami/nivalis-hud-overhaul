using System.Collections.Concurrent;

namespace NivalisMods.HudOverhaul;

internal sealed class HistoryWorker : IDisposable
{
    private readonly BlockingCollection<Action<HistoryStore>> _queue = new();
    private readonly Task _worker;
    private Exception? _failure;
    internal HistoryWorker(string path, Action<Exception> failure)
    {
        _worker = Task.Run(() =>
        {
            try
            {
                using var store = new HistoryStore(path);
                foreach (var job in _queue.GetConsumingEnumerable()) job(store);
            }
            catch (Exception e) { _failure = e; failure(e); }
        });
    }
    internal void Post(Action<HistoryStore> job)
    {
        if (_failure != null) throw new InvalidOperationException("History storage stopped after an error.", _failure);
        _queue.Add(job);
    }
    internal T Run<T>(Func<HistoryStore, T> job)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(db => { try { done.SetResult(job(db)); } catch (Exception e) { done.SetException(e); throw; } });
        var winner = Task.WhenAny(done.Task, _worker).GetAwaiter().GetResult();
        if (winner != done.Task) throw new InvalidOperationException("History worker stopped.", _failure);
        return done.Task.GetAwaiter().GetResult();
    }
    public void Dispose() { _queue.CompleteAdding(); _worker.GetAwaiter().GetResult(); _queue.Dispose(); }
}
