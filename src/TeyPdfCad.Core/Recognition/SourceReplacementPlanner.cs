using System.Globalization;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Semantics;

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

public sealed record SourceReplacementPlan(
    IReadOnlyCollection<string> SuppressedSourceIds,
    IReadOnlyCollection<string> PreservedSourceIds,
    IReadOnlyList<ReplacementConflict> Conflicts,
    IReadOnlyList<SourceClaim> ResidualClaims)
{
    public bool IsFullPassEligible => Conflicts.Count == 0 && ResidualClaims.Count == 0;
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

    private static SourceReplacementPlan Resolve(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
        IReadOnlyList<SourceClaim> claims)
    {
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var preserved = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = new List<ReplacementConflict>();
        var residuals = new List<SourceClaim>();

        var claimsBySource = claims
            .GroupBy(claim => claim.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var orphan in claimsBySource.Keys
                     .Where(sourceId => !sourceMap.ContainsKey(sourceId))
                     .OrderBy(sourceId => sourceId, StringComparer.Ordinal))
        {
            var orphanClaims = claimsBySource[orphan];
            conflicts.Add(new ReplacementConflict(
                orphan,
                ReplacementConflictReason.SourceMissingFromPage,
                "A semantic claim references a source object that is absent from page.Entities.",
                StableCandidateKeys(orphanClaims)));
            residuals.AddRange(orphanClaims);
        }

        foreach (var sourceId in sourceMap.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!claimsBySource.TryGetValue(sourceId, out var sourceClaims) || sourceClaims.Length == 0)
            {
                preserved.Add(sourceId);
                continue;
            }

            if (sourceClaims.Any(claim => claim.State == SourceClaimState.Unresolved))
            {
                PreserveConflict(sourceId, sourceClaims, ReplacementConflictReason.UnresolvedClaim,
                    "Unresolved semantic evidence blocks destructive source suppression.",
                    preserved, conflicts, residuals);
                continue;
            }

            if (sourceClaims.Any(claim => claim.IsPartial))
            {
                PreserveConflict(sourceId, sourceClaims, ReplacementConflictReason.PartialProvenance,
                    "Synthetic partial provenance never suppresses a whole source object.",
                    preserved, conflicts, residuals);
                continue;
            }

            var protectedClaims = sourceClaims
                .Where(claim => claim.Role is SourceUsageRole.HatchBoundary or SourceUsageRole.EvidenceOnly)
                .ToArray();
            var suppressibleClaims = sourceClaims.Except(protectedClaims).ToArray();

            if (protectedClaims.Length > 0)
            {
                preserved.Add(sourceId);
                if (suppressibleClaims.Length > 0)
                {
                    PreserveConflict(sourceId, sourceClaims, ReplacementConflictReason.ProtectedRoleOverlap,
                        "A protected source role overlaps a suppressible native replacement.",
                        preserved, conflicts, residuals);
                }
                continue;
            }

            var candidateKeys = sourceClaims
                .Select(claim => claim.CandidateKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (candidateKeys.Length > 1)
            {
                PreserveConflict(sourceId, sourceClaims, ReplacementConflictReason.MultipleCandidates,
                    "Multiple native candidates claim the same source object; preserve fidelity until role-specific sharing is proven safe.",
                    preserved, conflicts, residuals);
                continue;
            }

            suppressed.Add(sourceId);
        }

        return new SourceReplacementPlan(
            suppressed.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            preserved.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            conflicts.OrderBy(conflict => conflict.SourceId, StringComparer.Ordinal)
                .ThenBy(conflict => conflict.Reason).ToArray(),
            residuals.OrderBy(claim => claim.SourceId, StringComparer.Ordinal)
                .ThenBy(claim => claim.CandidateKey, StringComparer.Ordinal).ToArray());
    }

    private static void AddSemanticClaims(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
        SemanticReconstructionResult semantics,
        ICollection<SourceClaim> claims)
    {
        foreach (var candidate in semantics.Dimensions)
            AddCandidate(sourceMap, claims, "DIMENSION", StableKey("DIMENSION", candidate.ProvenanceIds, candidate.SourceText), candidate.ProvenanceIds);
        foreach (var candidate in semantics.Leaders)
            AddCandidate(sourceMap, claims, "LEADER", StableKey("LEADER", candidate.ProvenanceIds, candidate.Text), candidate.ProvenanceIds);
        foreach (var candidate in semantics.Axes)
            AddCandidate(sourceMap, claims, "AXIS", StableKey("AXIS", candidate.ProvenanceIds, candidate.Start.X, candidate.Start.Y, candidate.End.X, candidate.End.Y), candidate.ProvenanceIds);
        foreach (var candidate in semantics.Levels)
            AddCandidate(sourceMap, claims, "LEVEL", StableKey("LEVEL", candidate.ProvenanceIds, candidate.Value), candidate.ProvenanceIds);
        foreach (var candidate in semantics.ArcDimensions)
            AddCandidate(sourceMap, claims, "ARC_DIMENSION", StableKey("ARC_DIMENSION", candidate.ProvenanceIds, candidate.SourceText), candidate.ProvenanceIds);

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
            var key = StableKey("HATCH", provenance, hatch.PatternAngleRadians ?? 0d, hatch.PatternSpacingMillimetres ?? 0d);
            AddSingleClaim(sourceMap, claims, provenance[0], key, "HATCH", SourceUsageRole.HatchBoundary, SourceClaimState.Valid);
            foreach (var sourceId in provenance.Skip(1))
                AddSingleClaim(sourceMap, claims, sourceId, key, "HATCH", SourceUsageRole.HatchPattern, SourceClaimState.Valid);
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
        var candidateKey = StableKey(semanticType + ":" + warning.Code, warning.ProvenanceIds, warning.Message);
        foreach (var sourceId in warning.ProvenanceIds)
        {
            var parsed = ParseProvenance(sourceId);
            claims.Add(new SourceClaim(
                parsed.SourceId,
                candidateKey,
                semanticType,
                SourceUsageRole.EvidenceOnly,
                SourceClaimState.Unresolved,
                parsed.IsPartial));
        }
    }

    private static void AddSingleClaim(
        IReadOnlyDictionary<string, VectorEntity> sourceMap,
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

    private static void PreserveConflict(
        string sourceId,
        IReadOnlyList<SourceClaim> sourceClaims,
        ReplacementConflictReason reason,
        string detail,
        ISet<string> preserved,
        ICollection<ReplacementConflict> conflicts,
        ICollection<SourceClaim> residuals)
    {
        preserved.Add(sourceId);
        conflicts.Add(new ReplacementConflict(
            sourceId,
            reason,
            detail,
            StableCandidateKeys(sourceClaims)));
        foreach (var claim in sourceClaims)
            residuals.Add(claim);
    }

    private static IReadOnlyList<string> StableCandidateKeys(IEnumerable<SourceClaim> claims)
        => claims.Select(claim => claim.CandidateKey)
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
