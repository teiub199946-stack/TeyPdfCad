using System.Collections.Concurrent;

namespace TeyPdfCad.Web.Jobs;

public sealed class InMemoryConversionJobRegistry : IConversionJobRegistry
{
    private sealed record Entry(ConversionJob Job, DateTimeOffset LastTerminalAt);
    private readonly ConcurrentDictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly object _capacityGate = new();
    private readonly int _capacity;

    public InMemoryConversionJobRegistry(int capacity = 10_000)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryAdd(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_capacityGate)
        {
            if (_jobs.ContainsKey(job.JobId) || _jobs.Count >= _capacity)
                return false;
            return _jobs.TryAdd(job.JobId, new Entry(job, DateTimeOffset.MaxValue));
        }
    }

    public bool TryGet(string jobId, out ConversionJob? job)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryGetValue(jobId, out var entry))
        {
            job = null;
            return false;
        }
        job = entry.Job;
        return true;
    }

    public bool TryRemove(string jobId, out ConversionJob? job)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryRemove(jobId, out var entry))
        {
            job = null;
            return false;
        }
        job = entry.Job;
        return true;
    }

    public void MarkTerminal(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        while (_jobs.TryGetValue(job.JobId, out var current))
        {
            if (!string.Equals(current.Job.IdempotencyKey, job.IdempotencyKey, StringComparison.Ordinal))
                return;
            var updated = current with { LastTerminalAt = DateTimeOffset.UtcNow };
            if (_jobs.TryUpdate(job.JobId, updated, current))
                return;
        }
    }

    public int RemoveTerminalOlderThan(TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age));
        var cutoff = DateTimeOffset.UtcNow - age;
        var removed = 0;
        foreach (var pair in _jobs)
        {
            if (pair.Value.LastTerminalAt > cutoff)
                continue;
            if (_jobs.TryRemove(new KeyValuePair<string, Entry>(pair.Key, pair.Value)))
                removed++;
        }
        return removed;
    }
}
