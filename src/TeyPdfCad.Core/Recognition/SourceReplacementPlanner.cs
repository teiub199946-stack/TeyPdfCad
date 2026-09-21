using System.Globalization;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public enum SourceUsageRole
{
    PrimaryGeometry,
    Text,
    HatchPattern,
    HatchBoundary,
    EvidenceOnly
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
    SourceMissingFromPage
}

public enum ReplacementResidualKind
{
    DeferredShared,
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
    IReadOnlyCollection<string> SuppressedSourceIds,
    IReadOnlyCollection<string> PreservedSourceIds,
    IReadOnlyCollection<string> DeferredCandidateKeys,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SourceCoverageMap,
    IReadOnlyList<ReplacementConflict> Conflicts,
    IReadOnlyList<ReplacementResidual> Residuals)
{
    public bool IsFullPassEligible =>
        DeferredCandidateKeys.Count == 0
        && Conflicts.Count == 0
        && Residuals.Count == 0;
}

public sealed class SourceReplacementPlanner
{
    public SourceReplacementPlan BuildPlan(
        IReadOnlyList<VectorEntity> sources,
        SemanticReconstructionResult? semantics,
        HatchRecognitionResult? hatchRecognition = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var sourceMap = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceId))
            .GroupBy(source => source.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var claims = new List<SourceClaim>();

        if (semantics is not null)
            AddSemanticClaims(sourceMap, semantics, claims);
        if (hatchRecognition is not null)
            AddHatchClaims(sourceMap, hatchRecognition, claims);

        return Resolve(sourceMap, claims);
    }

    public static string GetCandidateKey(DimensionCandidate candidate)
        => StableKey("DIMENSION", candidate.ProvenanceIds, candidate.SourceText);

    public static string GetCandidateKey(LeaderCandidate candidate)
        => StableKey("LEADER", candidate.ProvenanceIds, candidate.Text);

    public static string GetCandidateKey(AxisCandidate candidate)
        => StableKey("AXIS", candidate.ProvenanceIds, candidate.Start.X, candidate.Start.Y, candidate.End.X, candidate.End.Y);

    public static string GetCandidateKey(LevelCandidate candidate)
        => StableKey("LEVEL", candidate.ProvenanceIds, candidate.Value);

    public static string GetCandidateKey(ArcDimensionCandidate candidate)
        => StableKey("ARC_DIMENSION", candidate.ProvenanceIds, candidate.SourceText);

    public static string GetCandidateKey(HatchCandidate candidate)
        => StableKey("HATCH", candidate.ProvenanceIds, candidate.PatternAngleRadians ?? 0d, candidate.PatternSpacingMillimetres ?? 0d);

    private static SourceReplacementPlan Resolve(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
        IReadOnlyList<SourceClaim> claims)
    {
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var preserved = new HashSet<string>(StringComparer.Ordinal);
        var deferred = new HashSet<string>(StringComparer.Ordinal);
        var coverage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var conflicts = new List<ReplacementConflict>();
        var residuals = new List<ReplacementResidual>();
        var deferredReasons = new Dictionary<string, ReplacementResidualKind>(StringComparer.Ordinal);

        var claimsBySource = claims
            .GroupBy(claim => claim.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        // Phase 1: discover candidate-level conflicts. If one source part makes a
        // candidate unsafe, defer the whole candidate. This is deliberately
        // conservative until SubEntityRef supports part-level replacement.
        foreach (var pair in claimsBySource.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var sourceId = pair.Key;
            var sourceClaims = pair.Value;
            var validClaims = sourceClaims.Where(claim => claim.State == SourceClaimState.Valid).ToArray();
            var validKeys = validClaims.Select(claim => claim.CandidateKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            if (!sourceMap.ContainsKey(sourceId))
            {
                Defer(validKeys, ReplacementResidualKind.SourceMissingFromPage, deferred, deferredReasons);
                AddConflict(conflicts, sourceId, ReplacementConflictReason.SourceMissingFromPage,
                    "A semantic claim references a source object that is absent from page.Entities.", sourceClaims);
                AddResidual(residuals, sourceId, null, ReplacementResidualKind.SourceMissingFromPage,
                    ReplacementResidualSeverity.Critical,
                    "Semantic provenance references a source object that is absent from the page.");
                continue;
            }

            if (sourceClaims.Any(claim => claim.State == SourceClaimState.Unresolved))
            {
                Defer(validKeys, ReplacementResidualKind.Unresolved, deferred, deferredReasons);
                AddConflict(conflicts, sourceId, ReplacementConflictReason.UnresolvedClaim,
                    "Unresolved semantic evidence blocks destructive source suppression.", sourceClaims);
                AddResidual(residuals, sourceId, null, ReplacementResidualKind.Unresolved,
                    validClaims.Any(claim => claim.Role == SourceUsageRole.PrimaryGeometry)
                        ? ReplacementResidualSeverity.High
                        : ReplacementResidualSeverity.Medium,
                    "Unresolved evidence overlaps this source object.");
                continue;
            }

            if (sourceClaims.Any(claim => claim.IsPartial))
            {
                Defer(validKeys, ReplacementResidualKind.PartialProvenance, deferred, deferredReasons);
                AddConflict(conflicts, sourceId, ReplacementConflictReason.PartialProvenance,
                    "Synthetic partial provenance never suppresses a whole source object.", sourceClaims);
                AddResidual(residuals, sourceId, null, ReplacementResidualKind.PartialProvenance,
                    ReplacementResidualSeverity.High,
                    "A partial source reference prevents whole-object replacement.");
                continue;
            }

            var protectedClaims = validClaims
                .Where(claim => claim.Role is SourceUsageRole.HatchBoundary or SourceUsageRole.EvidenceOnly)
                .ToArray();
            var suppressibleClaims = validClaims.Except(protectedClaims).ToArray();
            if (protectedClaims.Length > 0 && suppressibleClaims.Length > 0)
            {
                Defer(validKeys, ReplacementResidualKind.ProtectedRoleOverlap, deferred, deferredReasons);
                AddConflict(conflicts, sourceId, ReplacementConflictReason.ProtectedRoleOverlap,
                    "A protected source role overlaps a suppressible native replacement.", sourceClaims);
                AddResidual(residuals, sourceId, null, ReplacementResidualKind.ProtectedRoleOverlap,
                    ReplacementResidualSeverity.High,
                    "Protected visible geometry overlaps a native replacement candidate.");
                continue;
            }

            if (validKeys.Length > 1)
            {
                Defer(validKeys, ReplacementResidualKind.DeferredShared, deferred, deferredReasons);
                AddConflict(conflicts, sourceId, ReplacementConflictReason.MultipleCandidates,
                    "Multiple native candidates claim the same source object; all involved candidates are deferred.", sourceClaims);
                foreach (var key in validKeys)
                {
                    AddResidual(residuals, sourceId, key, ReplacementResidualKind.DeferredShared,
                        ReplacementResidualSeverity.High,
                        "Candidate was recognized but deferred because it shares source geometry with another native candidate.");
                }
            }
        }

        // Phase 2: resolve each source after candidate-level deferral has
        // propagated across every SourceId referenced by that candidate.
        foreach (var sourceId in sourceMap.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!claimsBySource.TryGetValue(sourceId, out var sourceClaims) || sourceClaims.Length == 0)
            {
                preserved.Add(sourceId);
                continue;
            }

            var validClaims = sourceClaims.Where(claim => claim.State == SourceClaimState.Valid).ToArray();
            var deferredClaims = validClaims.Where(claim => deferred.Contains(claim.CandidateKey)).ToArray();
            if (deferredClaims.Length > 0)
            {
                preserved.Add(sourceId);
                foreach (var claim in deferredClaims)
                {
                    var kind = deferredReasons.TryGetValue(claim.CandidateKey, out var reason)
                        ? reason
                        : ReplacementResidualKind.DeferredShared;
                    AddResidual(residuals, sourceId, claim.CandidateKey, kind,
                        kind == ReplacementResidualKind.SourceMissingFromPage
                            ? ReplacementResidualSeverity.Critical
                            : ReplacementResidualSeverity.High,
                        "Candidate-level deferral preserves every source object used by this candidate.");
                }
                continue;
            }

            if (sourceClaims.Any(claim => claim.State == SourceClaimState.Unresolved)
                || sourceClaims.Any(claim => claim.IsPartial))
            {
                preserved.Add(sourceId);
                continue;
            }

            if (validClaims.Any(claim => claim.Role is SourceUsageRole.HatchBoundary or SourceUsageRole.EvidenceOnly))
            {
                preserved.Add(sourceId);
                continue;
            }

            var candidateKeys = validClaims.Select(claim => claim.CandidateKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (candidateKeys.Length == 1)
            {
                suppressed.Add(sourceId);
                coverage[sourceId] = candidateKeys;
            }
            else
            {
                preserved.Add(sourceId);
            }
        }

        return new SourceReplacementPlan(
            suppressed.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            preserved.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            deferred.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            conflicts.OrderBy(conflict => conflict.SourceId, StringComparer.Ordinal)
                .ThenBy(conflict => conflict.Reason).ToArray(),
            residuals.OrderBy(residual => residual.SourceId, StringComparer.Ordinal)
                .ThenBy(residual => residual.CandidateKey, StringComparer.Ordinal)
                .ThenBy(residual => residual.Kind).ToArray());
    }

    private static void AddSemanticClaims(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
        SemanticReconstructionResult semantics,
        ICollection<SourceClaim> claims)
    {
        foreach (var candidate in semantics.Dimensions)
            AddCandidate(sourceMap, claims, "DIMENSION", GetCandidateKey(candidate), candidate.ProvenanceIds);
        foreach (var candidate in semantics.Leaders)
            AddCandidate(sourceMap, claims, "LEADER", GetCandidateKey(candidate), candidate.ProvenanceIds);
        foreach (var candidate in semantics.Axes)
            AddCandidate(sourceMap, claims, "AXIS", GetCandidateKey(candidate), candidate.ProvenanceIds);
        foreach (var candidate in semantics.Levels)
            AddCandidate(sourceMap, claims, "LEVEL", GetCandidateKey(candidate), candidate.ProvenanceIds);
        foreach (var candidate in semantics.ArcDimensions)
            AddCandidate(sourceMap, claims, "ARC_DIMENSION", GetCandidateKey(candidate), candidate.ProvenanceIds);

        foreach (var warning in semantics.Warnings)
            AddWarningClaims(claims, warning, "SEMANTIC_WARNING");
    }

    private static void AddHatchClaims(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
        HatchRecognitionResult hatchRecognition,
        ICollection<SourceClaim> claims)
    {
        foreach (var hatch in hatchRecognition.NativeHatches.Where(candidate => !candidate.IsSolid))
        {
            var provenance = hatch.ProvenanceIds;
            if (provenance.Count == 0) continue;
            var key = GetCandidateKey(hatch);
            AddSingleClaim(claims, provenance[0], key, "HATCH", SourceUsageRole.HatchBoundary, SourceClaimState.Valid);
            foreach (var sourceId in provenance.Skip(1))
                AddSingleClaim(claims, sourceId, key, "HATCH", SourceUsageRole.HatchPattern, SourceClaimState.Valid);
        }

        foreach (var warning in hatchRecognition.Warnings)
            AddWarningClaims(claims, warning, "HATCH_WARNING");
    }

    private static void AddCandidate(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
        ICollection<SourceClaim> claims,
        string semanticType,
        string candidateKey,
        IReadOnlyList<string> provenance)
    {
        foreach (var sourceId in provenance)
        {
            var parsed = ParseProvenance(sourceId);
            var role = sourceMap.TryGetValue(parsed.SourceId, out var source) && source is VectorText
                ? SourceUsageRole.Text
                : SourceUsageRole.PrimaryGeometry;
            claims.Add(new SourceClaim(parsed.SourceId, candidateKey, semanticType, role, SourceClaimState.Valid, parsed.IsPartial));
        }
    }

    private static void AddWarningClaims(
        ICollection<SourceClaim> claims,
        SemanticWarning warning,
        string semanticType)
    {
        var warningKey = StableKey(semanticType + ":" + warning.Code, warning.ProvenanceIds, warning.Message);
        foreach (var sourceId in warning.ProvenanceIds)
        {
            var parsed = ParseProvenance(sourceId);
            claims.Add(new SourceClaim(
                parsed.SourceId,
                warningKey,
                semanticType,
                SourceUsageRole.EvidenceOnly,
                SourceClaimState.Unresolved,
                parsed.IsPartial));
        }
    }

    private static void AddSingleClaim(
        ICollection<SourceClaim> claims,
        string provenanceId,
        string candidateKey,
        string semanticType,
        SourceUsageRole role,
        SourceClaimState state)
    {
        var parsed = ParseProvenance(provenanceId);
        claims.Add(new SourceClaim(parsed.SourceId, candidateKey, semanticType, role, state, parsed.IsPartial));
    }

    private static (string SourceId, bool IsPartial) ParseProvenance(string provenanceId)
    {
        if (string.IsNullOrWhiteSpace(provenanceId))
            return (string.Empty, true);
        var marker = provenanceId.IndexOf('#');
        return marker < 0
            ? (provenanceId, false)
            : (provenanceId[..marker], true);
    }

    private static void Defer(
        IEnumerable<string> candidateKeys,
        ReplacementResidualKind reason,
        ISet<string> deferred,
        IDictionary<string, ReplacementResidualKind> deferredReasons)
    {
        foreach (var key in candidateKeys)
        {
            deferred.Add(key);
            if (!deferredReasons.ContainsKey(key))
                deferredReasons[key] = reason;
        }
    }

    private static void AddConflict(
        ICollection<ReplacementConflict> conflicts,
        string sourceId,
        ReplacementConflictReason reason,
        string detail,
        IReadOnlyList<SourceClaim> claims)
    {
        conflicts.Add(new ReplacementConflict(sourceId, reason, detail, StableCandidateKeys(claims)));
    }

    private static void AddResidual(
        ICollection<ReplacementResidual> residuals,
        string sourceId,
        string? candidateKey,
        ReplacementResidualKind kind,
        ReplacementResidualSeverity severity,
        string detail)
    {
        if (residuals.Any(existing =>
                string.Equals(existing.SourceId, sourceId, StringComparison.Ordinal)
                && string.Equals(existing.CandidateKey, candidateKey, StringComparison.Ordinal)
                && existing.Kind == kind))
            return;
        residuals.Add(new ReplacementResidual(sourceId, candidateKey, kind, severity, detail));
    }

    private static IReadOnlyList<string> StableCandidateKeys(IEnumerable<SourceClaim> claims)
        => claims.Where(claim => claim.State == SourceClaimState.Valid)
            .Select(claim => claim.CandidateKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static string StableKey(string prefix, IReadOnlyList<string> provenance, params object[] values)
    {
        var sourcePart = string.Join(",", provenance
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.Ordinal));
        var valuePart = string.Join("|", values.Select(value => value switch
        {
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        }));
        return prefix + "|" + sourcePart + "|" + valuePart;
    }
}
