namespace WebWritingTool.Web.HealthChecks;

using System.Collections.Concurrent;

public sealed class BackgroundWorkerHealthState
{
    private readonly ConcurrentDictionary<string, BackgroundWorkerSnapshot> _workers = new(StringComparer.Ordinal);

    public void MarkStarted(string workerName)
    {
        var now = DateTimeOffset.UtcNow;
        _workers.AddOrUpdate(
            workerName,
            new BackgroundWorkerSnapshot(workerName, BackgroundWorkerState.Running, now, now, null),
            (_, current) => current with
            {
                State = BackgroundWorkerState.Running,
                LastStartedAt = current.LastStartedAt ?? now,
                LastHeartbeatAt = now,
                LastStoppedAt = null
            });
    }

    public void MarkHeartbeat(string workerName)
    {
        var now = DateTimeOffset.UtcNow;
        _workers.AddOrUpdate(
            workerName,
            new BackgroundWorkerSnapshot(workerName, BackgroundWorkerState.Running, now, now, null),
            (_, current) => current with
            {
                State = BackgroundWorkerState.Running,
                LastHeartbeatAt = now,
                LastStoppedAt = null
            });
    }

    public void MarkStopped(string workerName)
    {
        var now = DateTimeOffset.UtcNow;
        _workers.AddOrUpdate(
            workerName,
            new BackgroundWorkerSnapshot(workerName, BackgroundWorkerState.Stopped, null, null, now),
            (_, current) => current with
            {
                State = BackgroundWorkerState.Stopped,
                LastStoppedAt = now
            });
    }

    public void MarkDisabled(string workerName)
    {
        _workers[workerName] = new BackgroundWorkerSnapshot(
            workerName,
            BackgroundWorkerState.Disabled,
            null,
            null,
            null);
    }

    public BackgroundWorkerSnapshot GetSnapshot(string workerName)
    {
        return _workers.TryGetValue(workerName, out var snapshot)
            ? snapshot
            : new BackgroundWorkerSnapshot(workerName, BackgroundWorkerState.NotStarted, null, null, null);
    }
}

public enum BackgroundWorkerState
{
    NotStarted,
    Running,
    Stopped,
    Disabled
}

public sealed record BackgroundWorkerSnapshot(
    string Name,
    BackgroundWorkerState State,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastStoppedAt);
