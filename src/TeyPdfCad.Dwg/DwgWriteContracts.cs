using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Dwg;

public readonly record struct PageSourceRef(
    int PageNumber,
    string SourceId)
{
    public override string ToString()
        => $"{PageNumber}:{SourceId}";
}

public sealed record SourceEmissionSummary(
    IReadOnlyDictionary<PageSourceRef, IReadOnlyDictionary<string, int>> OutputFingerprintCountsBySource)
{
    public IReadOnlyDictionary<string, int> GetExpectedSuppressionFingerprintMultiset(
        IEnumerable<PageSourceRef> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var merged = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in sources.Distinct())
        {
            if (!OutputFingerprintCountsBySource.TryGetValue(source, out var fingerprints))
            {
                throw new InvalidOperationException(
                    $"No source-emission inventory exists for {source}.");
            }

            foreach (var pair in fingerprints)
            {
                merged[pair.Key] = merged.TryGetValue(pair.Key, out var count)
                    ? checked(count + pair.Value)
                    : pair.Value;
            }
        }

        return merged;
    }
}

public sealed record DwgWriteResult(
    NativeWriteManifest Manifest,
    SourceEmissionSummary SourceEmissionSummary,
    int DiagnosticCreatedCandidateKeyCount);
