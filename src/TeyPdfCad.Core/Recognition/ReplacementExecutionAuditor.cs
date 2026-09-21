namespace TeyPdfCad.Core.Recognition;

public sealed record ReplacementExecutionReport(
    IReadOnlyCollection<string> CreatedCandidateKeys,
    IReadOnlyList<string> GeometryLostSourceIds,
    bool ReadBackConfirmed)
{
    public bool AnyGeometryLost => GeometryLostSourceIds.Count > 0;
}

public static class ReplacementExecutionAuditor
{
    public static ReplacementExecutionReport Build(
        SourceReplacementPlan plan,
        IReadOnlyCollection<string> createdCandidateKeys,
        bool readBackConfirmed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(createdCandidateKeys);

        var created = createdCandidateKeys.ToHashSet(StringComparer.Ordinal);
        var lost = plan.SuppressedSourceIds
            .Where(sourceId =>
                !plan.SourceCoverageMap.TryGetValue(sourceId, out var coveringCandidates)
                || coveringCandidates.Count == 0
                || !coveringCandidates.Any(created.Contains))
            .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
            .ToArray();

        return new ReplacementExecutionReport(
            created.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            lost,
            readBackConfirmed);
    }
}
