using System.Collections.Concurrent;

namespace TeyPdfCad.Web.Jobs;

public sealed class InMemoryConversionResultRegistry : IConversionResultRegistry
{
    private sealed record Entry(ConversionResult Result, DateTimeOffset StoredAt);
    private readonly ConcurrentDictionary<string, Entry> _results =
        new(StringComparer.Ordinal);

    public void Set(ConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.JobId))
            throw new ArgumentException("Conversion result job id is required.", nameof(result));

        _results[result.JobId] = new Entry(result, DateTimeOffset.UtcNow);
    }

    public bool TryGet(string jobId, out ConversionResult? result)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            result = null;
            return false;
        }

        if (_results.TryGetValue(jobId, out var entry))
        {
            result = entry.Result;
            return true;
        }
        result = null;
        return false;
    }

    public bool TryRemove(string jobId, out ConversionResult? result)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !_results.TryRemove(jobId, out var entry))
        {
            result = null;
            return false;
        }

        result = entry.Result;
        return true;
    }

    public int RemoveOlderThan(TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age));
        var cutoff = DateTimeOffset.UtcNow - age;
        var removed = 0;
        foreach (var pair in _results)
        {
            if (pair.Value.StoredAt > cutoff)
                continue;
            if (_results.TryRemove(new KeyValuePair<string, Entry>(pair.Key, pair.Value)))
                removed++;
        }
        return removed;
    }
}
