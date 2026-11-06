using System.Collections.Concurrent;

namespace ReleaseManager.Services;

public sealed record ReleaseActionProgressEvent(
    DateTimeOffset At,
    int Percent,
    string Stage,
    string Message,
    bool IsError = false);

public sealed record ReleaseActionProgressSnapshot(
    int ReleaseId,
    bool IsRunning,
    bool IsCompleted,
    bool HasError,
    bool IsCanceled,
    bool IsCancelRequested,
    int Percent,
    string Stage,
    string Message,
    IReadOnlyList<ReleaseActionProgressEvent> Events);

public sealed class ReleaseActionProgressStore
{
    private sealed class State
    {
        public readonly object Gate = new();
        public bool IsRunning = true;
        public bool IsCompleted;
        public bool HasError;
        public bool IsCanceled;
        public bool IsCancelRequested;
        public readonly CancellationTokenSource Cancellation = new();
        public int Percent;
        public string Stage = "Starting";
        public string Message = "Starting release actions.";
        public readonly List<ReleaseActionProgressEvent> Events = new();
    }

    private readonly ConcurrentDictionary<int, State> _states = new();
    private readonly ILogger<ReleaseActionProgressStore> _logger;

    public ReleaseActionProgressStore(ILogger<ReleaseActionProgressStore> logger) => _logger = logger;

    public bool Start(int releaseId)
    {
        var state = new State();
        while (true)
        {
            if (_states.TryGetValue(releaseId, out var existing))
            {
                lock (existing.Gate)
                {
                    if (existing.IsRunning) return false;
                }
                if (!_states.TryUpdate(releaseId, state, existing)) continue;
                existing.Cancellation.Dispose();
            }
            else if (!_states.TryAdd(releaseId, state)) continue;
            break;
        }
        Record(releaseId, 2, "Starting", "Release action started.");
        return true;
    }

    public CancellationToken GetCancellationToken(int releaseId) =>
        _states.TryGetValue(releaseId, out var state) ? state.Cancellation.Token : CancellationToken.None;

    public bool RequestCancel(int releaseId)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return false;
        lock (state.Gate)
        {
            if (!state.IsRunning || state.IsCancelRequested) return false;
            state.IsCancelRequested = true;
            state.Stage = "Canceling";
            state.Message = "Cancellation requested; stopping the current operation safely.";
            state.Events.Add(new ReleaseActionProgressEvent(DateTimeOffset.Now, state.Percent,
                "Canceling", state.Message));
            TrimEvents(state);
        }
        _logger.LogWarning("Release {ReleaseId} | Cancel requested.", releaseId);
        state.Cancellation.Cancel();
        return true;
    }

    public bool IsCancelRequested(int releaseId) =>
        _states.TryGetValue(releaseId, out var state) && state.Cancellation.IsCancellationRequested;

    public void Canceled(int releaseId, string message)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return;
        lock (state.Gate)
        {
            state.IsRunning = false;
            state.IsCompleted = true;
            state.IsCanceled = true;
            state.Stage = "Canceled";
            state.Message = message;
            state.Events.Add(new ReleaseActionProgressEvent(DateTimeOffset.Now, state.Percent, "Canceled", message));
            TrimEvents(state);
        }
        _logger.LogWarning("Release {ReleaseId} | Canceled | {Message}", releaseId, message);
    }

    public void Report(int releaseId, int percent, string stage, string message)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return;
        var entry = new ReleaseActionProgressEvent(DateTimeOffset.Now, percent, stage, message);
        lock (state.Gate)
        {
            state.Percent = Math.Clamp(percent, 0, 100);
            state.Stage = stage;
            state.Message = message;
            state.Events.Add(entry);
            TrimEvents(state);
        }
        _logger.LogInformation("Release {ReleaseId} | {Stage} | {Message}", releaseId, stage, message);
    }

    public void Complete(int releaseId, string message)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return;
        var entry = new ReleaseActionProgressEvent(DateTimeOffset.Now, 100, "Complete", message);
        lock (state.Gate)
        {
            state.IsRunning = false;
            state.IsCompleted = true;
            state.Percent = 100;
            state.Stage = "Complete";
            state.Message = message;
            state.Events.Add(entry);
            TrimEvents(state);
        }
        _logger.LogInformation("Release {ReleaseId} | Complete | {Message}", releaseId, message);
    }

    public void Fail(int releaseId, string message)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return;
        ReleaseActionProgressEvent entry;
        lock (state.Gate)
        {
            entry = new ReleaseActionProgressEvent(DateTimeOffset.Now, state.Percent, "Error", message, true);
            state.IsRunning = false;
            state.IsCompleted = true;
            state.HasError = true;
            state.Stage = "Error";
            state.Message = message;
            state.Events.Add(entry);
            TrimEvents(state);
        }
        _logger.LogError("Release {ReleaseId} | Error | {Message}", releaseId, message);
    }

    public ReleaseActionProgressSnapshot? Get(int releaseId)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return null;
        lock (state.Gate)
        {
            return new ReleaseActionProgressSnapshot(releaseId, state.IsRunning, state.IsCompleted,
                state.HasError, state.IsCanceled, state.IsCancelRequested, state.Percent,
                state.Stage, state.Message, state.Events.ToArray());
        }
    }

    private void Record(int releaseId, int percent, string stage, string message)
    {
        if (!_states.TryGetValue(releaseId, out var state)) return;
        lock (state.Gate)
        {
            state.Percent = percent;
            state.Stage = stage;
            state.Message = message;
            state.Events.Add(new ReleaseActionProgressEvent(DateTimeOffset.Now, percent, stage, message));
        }
        _logger.LogInformation("Release {ReleaseId} | {Stage} | {Message}", releaseId, stage, message);
    }

    private static void TrimEvents(State state)
    {
        if (state.Events.Count > 100) state.Events.RemoveRange(0, state.Events.Count - 100);
    }
}
