using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TeyPdfCad.Web.Jobs;

public sealed class InMemoryConversionJobQueue : IConversionJobQueue
{
    private readonly Channel<ConversionJob> _pending;
    private readonly ConcurrentDictionary<string, ConversionJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConversionJobStatus> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _terminalAt = new(StringComparer.Ordinal);

    public InMemoryConversionJobQueue(int capacity = 128)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _pending = Channel.CreateBounded<ConversionJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask<ConversionJobEnqueueResult> EnqueueAsync(
        ConversionJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_statuses.TryAdd(job.IdempotencyKey, ConversionJobStatus.Queued))
            return ValueTask.FromResult(ConversionJobEnqueueResult.Duplicate);

        _jobs.TryAdd(job.IdempotencyKey, job);
        if (_pending.Writer.TryWrite(job))
            return ValueTask.FromResult(ConversionJobEnqueueResult.Accepted);

        _jobs.TryRemove(job.IdempotencyKey, out _);
        _statuses.TryRemove(job.IdempotencyKey, out _);
        _terminalAt.TryRemove(job.IdempotencyKey, out _);
        return ValueTask.FromResult(ConversionJobEnqueueResult.CapacityExceeded);
    }

    public async ValueTask<ConversionJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var job = await _pending.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!_statuses.ContainsKey(job.IdempotencyKey))
                continue;
            if (_statuses.TryUpdate(job.IdempotencyKey, ConversionJobStatus.Processing, ConversionJobStatus.Queued))
                return job;

            if (!_statuses.TryGetValue(job.IdempotencyKey, out var status))
                continue;
            if (status == ConversionJobStatus.Cancelled)
                continue;

            throw new InvalidOperationException("Queued job has an invalid lifecycle status.");
        }
    }

    public bool TryComplete(ConversionJob job, ConversionJobStatus status)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (status is not (ConversionJobStatus.Completed or ConversionJobStatus.Failed or ConversionJobStatus.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(status), "Only terminal statuses can complete a job.");

        var updated = _statuses.TryUpdate(job.IdempotencyKey, status, ConversionJobStatus.Processing);
        if (updated)
            _terminalAt[job.IdempotencyKey] = DateTimeOffset.UtcNow;
        return updated;
    }

    public bool TryGetByIdempotencyKey(string idempotencyKey, out ConversionJob? job)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        return _jobs.TryGetValue(idempotencyKey, out job);
    }

    public bool TryCancel(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var updated = _statuses.TryUpdate(job.IdempotencyKey, ConversionJobStatus.Cancelled, ConversionJobStatus.Queued);
        if (updated)
            _terminalAt[job.IdempotencyKey] = DateTimeOffset.UtcNow;
        return updated;
    }

    public ConversionJobStatus GetStatus(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _statuses.TryGetValue(job.IdempotencyKey, out var status)
            ? status
            : throw new KeyNotFoundException($"Job '{job.JobId}' is not queued.");
    }

    public int RemoveTerminalOlderThan(TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age));
        var cutoff = DateTimeOffset.UtcNow - age;
        var removed = 0;
        foreach (var pair in _terminalAt)
        {
            if (pair.Value > cutoff || !_statuses.TryGetValue(pair.Key, out var status) ||
                status is not (ConversionJobStatus.Completed or ConversionJobStatus.Failed or ConversionJobStatus.Cancelled))
                continue;
            if (_terminalAt.TryRemove(new KeyValuePair<string, DateTimeOffset>(pair.Key, pair.Value)))
            {
                _statuses.TryRemove(pair.Key, out _);
                _jobs.TryRemove(pair.Key, out _);
                removed++;
            }
        }
        return removed;
    }
}
