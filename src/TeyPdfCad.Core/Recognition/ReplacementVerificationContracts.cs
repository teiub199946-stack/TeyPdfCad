using System.Collections.ObjectModel;

namespace TeyPdfCad.Core.Recognition;

public enum SourceUsageRole
{
    DimensionLine,
    ExtensionLine,
    ArrowGeometry,
    LeaderShaft,
    LeaderArrow,
    LeaderLanding,
    AxisGeometry,
    LevelMarker,
    Text,
    HatchPattern,
    HatchBoundary,
    EvidenceOnly,

    // Transitional diagnostic value only. P0 suppression never treats this
    // legacy catch-all role as suppressible.
    PrimaryGeometry
}

public enum SourceClaimState
{
    Valid,
    Unresolved
}

public enum ReplacementConflictReason
{
    UnresolvedClaim,
    PartialProvenance,
    ProtectedRoleOverlap,
    MultipleCandidates,
    SourceMissingFromPage,
    SourceIdentityViolation
}

public enum ReplacementResidualKind
{
    DeferredShared,
    DeferredUnresolvedClaims,
    DeferredUncertainHatch,
    CandidateNotVerified,
    SourceIdentityViolation,
    SourceSuppressionViolation,

    // Legacy/reporting kinds kept while the remaining P0 stages are migrated.
    Unrecognized,
    Unresolved,
    PartialProvenance,
    ProtectedRoleOverlap,
    SourceMissingFromPage
}

public enum ReplacementResidualSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record RecognizerSourceClaim(
    string SourceId,
    SourceUsageRole Role,
    SourceClaimState State,
    bool IsPartial);

public static class RecognizerSourceClaimBuilder
{
    public static IReadOnlyList<RecognizerSourceClaim> FromProvenance(
        SourceUsageRole role,
        params IReadOnlyList<string>[] provenanceGroups)
        => provenanceGroups
            .SelectMany(group => group ?? [])
            .Where(value => value is not null)
            .Select(value => Parse(value, role))
            .Distinct()
            .OrderBy(claim => claim.SourceId, StringComparer.Ordinal)
            .ThenBy(claim => claim.Role)
            .ThenBy(claim => claim.IsPartial)
            .ToArray();

    private static RecognizerSourceClaim Parse(string provenanceId, SourceUsageRole role)
    {
        if (string.IsNullOrWhiteSpace(provenanceId))
            return new RecognizerSourceClaim(string.Empty, role, SourceClaimState.Unresolved, true);

        var marker = provenanceId.IndexOf('#');
        return marker < 0
            ? new RecognizerSourceClaim(provenanceId, role, SourceClaimState.Valid, false)
            : new RecognizerSourceClaim(
                provenanceId[..marker],
                role,
                SourceClaimState.Valid,
                true);
    }
}

public sealed record SourceClaim(
    string SourceId,
    string CandidateKey,
    string SemanticType,
    SourceUsageRole Role,
    SourceClaimState State,
    bool IsPartial);

public sealed record ReplacementConflict(
    string SourceId,
    ReplacementConflictReason Reason,
    string Detail,
    IReadOnlyList<string> CandidateKeys);

public sealed record ReplacementResidual(
    string SourceId,
    string? CandidateKey,
    ReplacementResidualKind Kind,
    ReplacementResidualSeverity Severity,
    string Detail);

public sealed record SourceReplacementPlan(
    IReadOnlyCollection<string> EligibleSourceIds,
    IReadOnlyCollection<string> PreservedSourceIds,
    IReadOnlyCollection<string> DeferredCandidateKeys,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SourceCoverageMap,
    IReadOnlyList<ReplacementConflict> Conflicts,
    IReadOnlyList<ReplacementResidual> Residuals)
{
    // Transitional compatibility for downstream code that is migrated in later
    // P0 tasks. New P0 code must use EligibleSourceIds + SuppressionGate.
    [Obsolete("P0 replacement planning produces eligibility, not authorization. Use EligibleSourceIds and SuppressionGate.")]
    public IReadOnlyCollection<string> SuppressedSourceIds => EligibleSourceIds;

    public bool HasCompleteCoveragePlan =>
        EligibleSourceIds.All(sourceId =>
            SourceCoverageMap.TryGetValue(sourceId, out var candidates)
            && candidates.Count > 0);

    public bool IsFullPassEligible =>
        DeferredCandidateKeys.Count == 0
        && Conflicts.Count == 0
        && Residuals.Count == 0
        && HasCompleteCoveragePlan;
}

public sealed record ExpectedNativeEntity(
    string CandidateId,
    string Role,
    string EntityKind,
    string GeometryFingerprint,
    IReadOnlyDictionary<string, string> RequiredProperties);

public sealed record ExpectedCandidate(
    string CandidateId,
    string SemanticType,
    IReadOnlyList<ExpectedNativeEntity> Entities);

public sealed record NativeWriteManifest(
    IReadOnlyDictionary<string, ExpectedCandidate> Candidates);

public sealed record CandidateVerification(
    string CandidateId,
    bool IsVerified,
    IReadOnlyList<string> MissingRoles,
    IReadOnlyList<string> DuplicateRoles,
    IReadOnlyList<string> InvalidEntities);

public sealed record NativeReadBackVerification(
    IReadOnlyDictionary<string, CandidateVerification> Candidates);

public sealed record SuppressionDecision(
    IReadOnlySet<string> SuppressSourceIds,
    IReadOnlySet<string> PreserveSourceIds,
    IReadOnlyList<ReplacementResidual> Residuals);

public sealed class SuppressionGate
{
    public SuppressionDecision Evaluate(
        SourceReplacementPlan plan,
        NativeReadBackVerification verification)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verification);

        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var preserved = new HashSet<string>(plan.PreservedSourceIds, StringComparer.Ordinal);
        var residuals = plan.Residuals.ToList();

        foreach (var sourceId in plan.EligibleSourceIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!plan.SourceCoverageMap.TryGetValue(sourceId, out var candidateIds)
                || candidateIds.Count != 1)
            {
                preserved.Add(sourceId);
                AddResidual(
                    residuals,
                    sourceId,
                    null,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Source eligibility is missing an unambiguous candidate mapping.");
                continue;
            }

            var candidateId = candidateIds[0];
            if (verification.Candidates.TryGetValue(candidateId, out var candidate)
                && candidate.IsVerified)
            {
                suppressed.Add(sourceId);
                continue;
            }

            preserved.Add(sourceId);
            AddResidual(
                residuals,
                sourceId,
                candidateId,
                ReplacementResidualKind.CandidateNotVerified,
                ReplacementResidualSeverity.Critical,
                "Native candidate was not independently verified from DWG read-back; source is preserved.");
        }

        return new SuppressionDecision(
            suppressed,
            preserved,
            residuals
                .OrderBy(residual => residual.SourceId, StringComparer.Ordinal)
                .ThenBy(residual => residual.CandidateKey, StringComparer.Ordinal)
                .ThenBy(residual => residual.Kind)
                .ToArray());
    }

    private static void AddResidual(
        ICollection<ReplacementResidual> residuals,
        string sourceId,
        string? candidateId,
        ReplacementResidualKind kind,
        ReplacementResidualSeverity severity,
        string detail)
    {
        if (residuals.Any(existing =>
                string.Equals(existing.SourceId, sourceId, StringComparison.Ordinal)
                && string.Equals(existing.CandidateKey, candidateId, StringComparison.Ordinal)
                && existing.Kind == kind))
            return;

        residuals.Add(new ReplacementResidual(sourceId, candidateId, kind, severity, detail));
    }
}

public enum HatchClassification
{
    Confident,
    Uncertain
}

public sealed record HatchClaim(
    string CandidateId,
    IReadOnlyList<string> BoundarySourceIds,
    IReadOnlyList<string> PatternSourceIds,
    HatchClassification Classification);
