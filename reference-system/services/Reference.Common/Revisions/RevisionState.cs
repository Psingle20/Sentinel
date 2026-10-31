namespace Reference.Common.Revisions;

/// <summary>
/// The revision a reference service is serving. The deploy controller switches it to stand for a
/// deployment; each revision selects a healthy or faulty behaviour inside the service.
/// </summary>
public sealed class RevisionState
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly List<Func<RevisionChange, Task>> _handlers = [];

    public RevisionState(string serviceName, string initialRevision, IReadOnlyCollection<string> knownRevisions, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(knownRevisions);

        if (!knownRevisions.Contains(initialRevision))
        {
            throw new ArgumentException($"{initialRevision} is not a known revision of {serviceName}.", nameof(initialRevision));
        }

        ServiceName = serviceName;
        KnownRevisions = knownRevisions;
        _time = time;
        Current = initialRevision;
        ActiveSince = time.GetUtcNow();
    }

    public string ServiceName { get; }

    public IReadOnlyCollection<string> KnownRevisions { get; }

    public string Current { get; private set; }

    public DateTimeOffset ActiveSince { get; private set; }

    /// <summary>Time the current revision has been serving.</summary>
    public TimeSpan Uptime => _time.GetUtcNow() - ActiveSince;

    /// <summary>Registers a callback run after each switch, so the service can drop state the old revision held.</summary>
    public void OnChanged(Func<RevisionChange, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handlers.Add(handler);
        }
    }

    /// <summary>Switches to <paramref name="revision"/>. Switching to the serving revision is a no-op.</summary>
    public async Task<bool> SwitchAsync(string revision)
    {
        if (!KnownRevisions.Contains(revision))
        {
            return false;
        }

        RevisionChange change;
        lock (_gate)
        {
            if (revision == Current)
            {
                return true;
            }

            change = new RevisionChange(Current, revision, _time.GetUtcNow());
            Current = revision;
            ActiveSince = change.ChangedAt;
        }

        Func<RevisionChange, Task>[] handlers;
        lock (_gate)
        {
            handlers = [.. _handlers];
        }

        foreach (Func<RevisionChange, Task> handler in handlers)
        {
            await handler(change);
        }

        return true;
    }
}
