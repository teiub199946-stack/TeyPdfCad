namespace TeyPdfCad.Core.Recognition;

public sealed record ReplacementExecutionReport(
    IReadOnlyList<string> CandidateNotVerifiedSourceIds,
    bool ReadBackConfirmed)
{
    // Compatibility aliases for the legacy report surface. Under P0 an
    // unverified candidate preserves its source, so this is not GeometryLost.
    public IReadOnlyList<string> GeometryLostSourceIds => [];
    public bool AnyGeometryLost => false;
}

public static class ReplacementExecutionAuditor
{
    public static ReplacementExecutionReport Build(
        SourceReplacementPlan plan,
        NativeReadBackVerification verification,
        bool readBackConfirmed = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verification);

        var notVerified = plan.EligibleSourceIds
            .Where(sourceId =>
                !plan.SourceCoverageMap.TryGetValue(sourceId, out var coveringCandidates)
                || coveringCandidates.Count != 1
                || !verification.Candidates.TryGetValue(coveringCandidates[0], out var candidate)
                || !candidate.IsVerified)
            .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
            .ToArray();

        return new ReplacementExecutionReport(notVerified, readBackConfirmed);
    }
}
