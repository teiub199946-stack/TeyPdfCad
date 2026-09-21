using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public sealed class SourceReplacementPlanner
{
    public SourceReplacementPlan BuildPlan(
        IReadOnlyList<VectorEntity> sources,
        SemanticReconstructionResult? semantics,
        HatchRecognitionResult? hatchRecognition = null,
        int pageNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var pageResiduals = new List<ReplacementResidual>();
        var conflicts = new List<ReplacementConflict>();
        var invalidSourceIds = ValidateSourceIdentity(sources, pageResiduals, conflicts);
        var sourceGroups = sources
            .GroupBy(source => source.SourceId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var candidates = new List<CandidateDescriptor>();
        var evidenceOnlySourceIds = new HashSet<string>(StringComparer.Ordinal);
        if (semantics is not null)
        {
            AddSemanticCandidates(candidates, semantics, pageNumber);
            AddWarningResiduals(pageResiduals, semantics.Warnings, "semantic");
            AddWarningEvidence(evidenceOnlySourceIds, semantics.Warnings);
        }

        if (hatchRecognition is not null)
        {
            AddHatchCandidates(candidates, hatchRecognition);
            AddWarningResiduals(pageResiduals, hatchRecognition.Warnings, "hatch");
            AddWarningEvidence(evidenceOnlySourceIds, hatchRecognition.Warnings);
        }

        return Resolve(
            sourceGroups,
            invalidSourceIds,
            evidenceOnlySourceIds,
            candidates,
            pageResiduals,
            conflicts);
    }

    public static string CreateCandidateId(
        int pageNumber,
        string semanticType,
        IEnumerable<string> sourceIds,
        string normalizedGeometryFingerprint)
    {
        if (pageNumber < 0) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (string.IsNullOrWhiteSpace(semanticType))
            throw new ArgumentException("Semantic type is required.", nameof(semanticType));
        if (normalizedGeometryFingerprint is null)
            throw new ArgumentNullException(nameof(normalizedGeometryFingerprint));

        var normalizedSources = sourceIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var canonical = new StringBuilder()
            .Append("v1").Append('\n')
            .Append(pageNumber.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(semanticType).Append('\n')
            .Append(normalizedSources.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');

        foreach (var sourceId in normalizedSources)
        {
            canonical
                .Append(sourceId.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(sourceId)
                .Append('\n');
        }

        canonical.Append(normalizedGeometryFingerprint);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        var suffix = Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        return $"v1:{pageNumber.ToString(CultureInfo.InvariantCulture)}:{semanticType}:{suffix}";
    }

    public static string GetCandidateKey(DimensionCandidate candidate, int pageNumber = 0)
        => CreateCandidateId(pageNumber, "DIMENSION", NormalizeDeclaredSourceIds(candidate.ProvenanceIds), Fingerprint(candidate));

    public static string GetCandidateKey(LeaderCandidate candidate, int pageNumber = 0)
        => CreateCandidateId(pageNumber, "LEADER", NormalizeDeclaredSourceIds(candidate.ProvenanceIds), Fingerprint(candidate));

    public static string GetCandidateKey(AxisCandidate candidate, int pageNumber = 0)
        => CreateCandidateId(pageNumber, "AXIS", NormalizeDeclaredSourceIds(candidate.ProvenanceIds), Fingerprint(candidate));

    public static string GetCandidateKey(LevelCandidate candidate, int pageNumber = 0)
        => CreateCandidateId(pageNumber, "LEVEL", NormalizeDeclaredSourceIds(candidate.ProvenanceIds), Fingerprint(candidate));

    public static string GetCandidateKey(ArcDimensionCandidate candidate, int pageNumber = 0)
        => CreateCandidateId(pageNumber, "ARC_DIMENSION", NormalizeDeclaredSourceIds(candidate.ProvenanceIds), Fingerprint(candidate));

    public static string GetCandidateKey(HatchCandidate candidate, int pageNumber = 0)
        => CreateCandidateId(pageNumber, "HATCH", candidate.ProvenanceIds, Fingerprint(candidate));

    private static IReadOnlyCollection<string> ValidateSourceIdentity(
        IReadOnlyList<VectorEntity> sources,
        ICollection<ReplacementResidual> residuals,
        ICollection<ReplacementConflict> conflicts)
    {
        var invalid = new HashSet<string>(StringComparer.Ordinal);
        var emptyCount = sources.Count(source => string.IsNullOrWhiteSpace(source.SourceId));
        if (emptyCount > 0)
        {
            invalid.Add(string.Empty);
            AddResidual(
                residuals,
                "(empty)",
                null,
                ReplacementResidualKind.SourceIdentityViolation,
                ReplacementResidualSeverity.Critical,
                $"{emptyCount} source object(s) have an empty SourceId; no related suppression is allowed.");
            conflicts.Add(new ReplacementConflict(
                "(empty)",
                ReplacementConflictReason.SourceIdentityViolation,
                "Every P0 source must have a non-empty page-unique SourceId.",
                []));
        }

        foreach (var group in sources
                     .Where(source => !string.IsNullOrWhiteSpace(source.SourceId))
                     .GroupBy(source => source.SourceId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            invalid.Add(group.Key);
            AddResidual(
                residuals,
                group.Key,
                null,
                ReplacementResidualKind.SourceIdentityViolation,
                ReplacementResidualSeverity.Critical,
                $"SourceId occurs {group.Count()} times on the page; all instances are preserved.");
            conflicts.Add(new ReplacementConflict(
                group.Key,
                ReplacementConflictReason.SourceIdentityViolation,
                "Duplicate page SourceId blocks source replacement.",
                []));
        }

        return invalid;
    }

    private static void AddSemanticCandidates(
        ICollection<CandidateDescriptor> output,
        SemanticReconstructionResult semantics,
        int pageNumber)
    {
        foreach (var candidate in semantics.Dimensions)
            output.Add(new CandidateDescriptor(
                GetCandidateKey(candidate, pageNumber),
                "DIMENSION",
                NormalizeClaims(candidate.SourceClaims),
                NormalizeDeclaredSourceIds(candidate.ProvenanceIds)));
        foreach (var candidate in semantics.Leaders)
            output.Add(new CandidateDescriptor(
                GetCandidateKey(candidate, pageNumber),
                "LEADER",
                NormalizeClaims(candidate.SourceClaims),
                NormalizeDeclaredSourceIds(candidate.ProvenanceIds)));
        foreach (var candidate in semantics.Axes)
            output.Add(new CandidateDescriptor(
                GetCandidateKey(candidate, pageNumber),
                "AXIS",
                NormalizeClaims(candidate.SourceClaims),
                NormalizeDeclaredSourceIds(candidate.ProvenanceIds)));
        foreach (var candidate in semantics.Levels)
            output.Add(new CandidateDescriptor(
                GetCandidateKey(candidate, pageNumber),
                "LEVEL",
                NormalizeClaims(candidate.SourceClaims),
                NormalizeDeclaredSourceIds(candidate.ProvenanceIds)));
        foreach (var candidate in semantics.ArcDimensions)
            output.Add(new CandidateDescriptor(
                GetCandidateKey(candidate, pageNumber),
                "ARC_DIMENSION",
                NormalizeClaims(candidate.SourceClaims),
                NormalizeDeclaredSourceIds(candidate.ProvenanceIds)));
    }

    private static void AddHatchCandidates(
        ICollection<CandidateDescriptor> output,
        HatchRecognitionResult hatchRecognition)
    {
        foreach (var hatch in hatchRecognition.Claims)
        {
            var claims = hatch.BoundarySourceIds
                .Select(sourceId => new RecognizerSourceClaim(
                    sourceId,
                    SourceUsageRole.HatchBoundary,
                    SourceClaimState.Valid,
                    false))
                .Concat(hatch.PatternSourceIds.Select(sourceId => new RecognizerSourceClaim(
                    sourceId,
                    SourceUsageRole.HatchPattern,
                    SourceClaimState.Valid,
                    false)))
                .ToArray();

            output.Add(new CandidateDescriptor(
                hatch.CandidateId,
                "HATCH",
                NormalizeClaims(claims),
                hatch.BoundarySourceIds
                    .Concat(hatch.PatternSourceIds)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                hatch.Classification));
        }
    }

    private static SourceReplacementPlan Resolve(
        IReadOnlyDictionary<string, VectorEntity[]> sourceGroups,
        IReadOnlyCollection<string> invalidSourceIds,
        IReadOnlyCollection<string> evidenceOnlySourceIds,
        IReadOnlyList<CandidateDescriptor> candidates,
        IReadOnlyList<ReplacementResidual> pageResiduals,
        IReadOnlyList<ReplacementConflict> initialConflicts)
    {
        var eligible = new HashSet<string>(StringComparer.Ordinal);
        var preserved = new HashSet<string>(StringComparer.Ordinal);
        var deferred = new HashSet<string>(StringComparer.Ordinal);
        var coverage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var conflicts = new List<ReplacementConflict>(initialConflicts);
        var residuals = new List<ReplacementResidual>(pageResiduals);

        foreach (var sourceId in sourceGroups.Keys)
        {
            if (string.IsNullOrWhiteSpace(sourceId)
                || invalidSourceIds.Contains(sourceId)
                || evidenceOnlySourceIds.Contains(sourceId))
                preserved.Add(sourceId);
        }

        foreach (var evidenceSourceId in evidenceOnlySourceIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (sourceGroups.ContainsKey(evidenceSourceId))
                continue;

            conflicts.Add(new ReplacementConflict(
                evidenceSourceId,
                ReplacementConflictReason.SourceMissingFromPage,
                "Warning evidence references a SourceId absent from page.Entities.",
                []));
            AddResidual(
                residuals,
                evidenceSourceId,
                null,
                ReplacementResidualKind.SourceMissingFromPage,
                ReplacementResidualSeverity.Critical,
                "Warning provenance references a source object absent from the page.");
        }

        var candidateGroups = candidates
            .GroupBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var uniqueCandidates = new List<CandidateDescriptor>();
        foreach (var group in candidateGroups)
        {
            var first = group.First();
            if (group.All(candidate => EquivalentCandidateDescriptor(first, candidate)))
            {
                uniqueCandidates.Add(first);
                continue;
            }

            deferred.Add(group.Key);
            foreach (var sourceId in group
                         .SelectMany(candidate => candidate.DeclaredSourceIds.Concat(candidate.Claims.Select(claim => claim.SourceId)))
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal))
            {
                preserved.Add(sourceId);
            }
            conflicts.Add(new ReplacementConflict(
                "(candidate)",
                ReplacementConflictReason.CandidateIdentityViolation,
                "Different candidate descriptors produced the same page-local CandidateId.",
                [group.Key]));
            AddResidual(
                residuals,
                "(candidate)",
                group.Key,
                ReplacementResidualKind.CandidateIdentityViolation,
                ReplacementResidualSeverity.Critical,
                "CandidateId collision was rejected before native emission.");
        }

        foreach (var candidate in uniqueCandidates.OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal))
        {
            if (candidate.HatchClassification == HatchClassification.Uncertain)
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.Claims.Select(claim => claim.SourceId).Where(id => !string.IsNullOrWhiteSpace(id)))
                    preserved.Add(sourceId);
                AddResidual(
                    residuals,
                    "(hatch)",
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUncertainHatch,
                    ReplacementResidualSeverity.Medium,
                    "Pattern HATCH lacks strong evidence; native emission is deferred and source geometry is preserved.");
                continue;
            }

            if (candidate.Claims.Count == 0 || candidate.DeclaredSourceIds.Count == 0)
            {
                deferred.Add(candidate.CandidateId);
                AddResidual(
                    residuals,
                    "(candidate)",
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Recognizer did not provide a non-empty explicit source-role claim set.");
                continue;
            }

            var claimSourceIds = candidate.Claims
                .Select(claim => claim.SourceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (!candidate.DeclaredSourceIds.SequenceEqual(claimSourceIds, StringComparer.Ordinal))
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.DeclaredSourceIds.Concat(claimSourceIds).Distinct(StringComparer.Ordinal))
                    preserved.Add(sourceId);
                AddResidual(
                    residuals,
                    candidate.DeclaredSourceIds.FirstOrDefault() ?? "(candidate)",
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Declared candidate source set does not equal the union of explicit recognizer claims.");
                continue;
            }

            var evidenceOverlap = candidate.DeclaredSourceIds
                .Where(evidenceOnlySourceIds.Contains)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (evidenceOverlap.Length > 0)
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.DeclaredSourceIds)
                    preserved.Add(sourceId);
                conflicts.Add(new ReplacementConflict(
                    evidenceOverlap[0],
                    ReplacementConflictReason.UnresolvedClaim,
                    "Unresolved warning evidence overlaps this candidate; whole-source suppression is blocked.",
                    [candidate.CandidateId]));
                AddResidual(
                    residuals,
                    evidenceOverlap[0],
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "EvidenceOnly warning provenance prevents native replacement eligibility.");
                continue;
            }

            var roleConflict = candidate.Claims
                .GroupBy(claim => claim.SourceId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Select(claim => claim.Role).Distinct().Count() > 1);
            if (roleConflict is not null)
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.DeclaredSourceIds)
                    preserved.Add(sourceId);
                conflicts.Add(new ReplacementConflict(
                    roleConflict.Key,
                    ReplacementConflictReason.ClaimRoleConflict,
                    "The same SourceId has multiple roles inside one candidate; whole-source P0 cannot resolve the ambiguity.",
                    [candidate.CandidateId]));
                AddResidual(
                    residuals,
                    roleConflict.Key,
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Ambiguous source role prevents whole-source replacement.");
                continue;
            }

            if (!TryGetRoleContract(candidate.SemanticType, out var roleContract))
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.DeclaredSourceIds)
                    preserved.Add(sourceId);
                AddResidual(
                    residuals,
                    candidate.DeclaredSourceIds.First(),
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Unknown semantic type has no P0 role contract and is fail-closed.");
                continue;
            }

            if (roleContract.Required.Any(required =>
                    candidate.Claims.All(claim => claim.Role != required))
                || candidate.Claims.Any(claim =>
                    !roleContract.Allowed.Contains(claim.Role)))
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.DeclaredSourceIds)
                    preserved.Add(sourceId);
                AddResidual(
                    residuals,
                    candidate.DeclaredSourceIds.First(),
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Candidate is missing a required role or contains a role not allowed for its semantic type.");
                continue;
            }

            var invalidClaim = false;
            foreach (var claim in candidate.Claims)
            {
                if (string.IsNullOrWhiteSpace(claim.SourceId)
                    || invalidSourceIds.Contains(claim.SourceId))
                {
                    invalidClaim = true;
                    continue;
                }

                if (!sourceGroups.ContainsKey(claim.SourceId))
                {
                    invalidClaim = true;
                    conflicts.Add(new ReplacementConflict(
                        claim.SourceId,
                        ReplacementConflictReason.SourceMissingFromPage,
                        "Recognizer claim references a SourceId absent from page.Entities.",
                        [candidate.CandidateId]));
                    AddResidual(
                        residuals,
                        claim.SourceId,
                        candidate.CandidateId,
                        ReplacementResidualKind.SourceMissingFromPage,
                        ReplacementResidualSeverity.Critical,
                        "Explicit recognizer claim references a source object absent from the page.");
                }
            }

            var unresolved = candidate.Claims.Any(claim =>
                claim.State == SourceClaimState.Unresolved
                || claim.IsPartial
                || claim.SourceId.Contains('#', StringComparison.Ordinal)
                || claim.Role is SourceUsageRole.Unknown
                    or SourceUsageRole.EvidenceOnly
                    or SourceUsageRole.PrimaryGeometry);
            var hasSuppressible = candidate.Claims.Any(claim => IsSuppressible(claim.Role));
            var protectedOverlap = candidate.Claims
                .GroupBy(claim => claim.SourceId, StringComparer.Ordinal)
                .Any(group => group.Any(claim => claim.Role == SourceUsageRole.HatchBoundary)
                    && group.Any(claim => IsSuppressible(claim.Role)));

            if (invalidClaim || unresolved || !hasSuppressible || protectedOverlap)
            {
                deferred.Add(candidate.CandidateId);
                foreach (var sourceId in candidate.Claims.Select(claim => claim.SourceId).Where(id => !string.IsNullOrWhiteSpace(id)))
                    preserved.Add(sourceId);

                if (protectedOverlap)
                {
                    conflicts.Add(new ReplacementConflict(
                        candidate.Claims.First(claim => claim.Role == SourceUsageRole.HatchBoundary).SourceId,
                        ReplacementConflictReason.ProtectedRoleOverlap,
                        "A protected HATCH boundary overlaps a suppressible claim in the same candidate.",
                        [candidate.CandidateId]));
                }

                AddResidual(
                    residuals,
                    candidate.Claims.Select(claim => claim.SourceId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? "(candidate)",
                    candidate.CandidateId,
                    ReplacementResidualKind.DeferredUnresolvedClaims,
                    ReplacementResidualSeverity.High,
                    "Incomplete, partial, legacy, protected-overlap, or unresolved recognizer claims prevent native emission.");
            }
        }

        var claimsBySource = uniqueCandidates
            .SelectMany(candidate => candidate.Claims
                .Where(claim => claim.State == SourceClaimState.Valid && !claim.IsPartial)
                .Select(claim => (candidate, claim)))
            .Where(item => !string.IsNullOrWhiteSpace(item.claim.SourceId))
            .GroupBy(item => item.claim.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var pair in claimsBySource.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var candidateIds = pair.Value
                .Select(item => item.candidate.CandidateId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (candidateIds.Length <= 1)
                continue;

            foreach (var candidateId in candidateIds)
                deferred.Add(candidateId);

            preserved.Add(pair.Key);
            conflicts.Add(new ReplacementConflict(
                pair.Key,
                ReplacementConflictReason.MultipleCandidates,
                "Multiple native candidates claim the same SourceId; every involved candidate is deferred.",
                candidateIds));
            foreach (var candidateId in candidateIds)
            {
                AddResidual(
                    residuals,
                    pair.Key,
                    candidateId,
                    ReplacementResidualKind.DeferredShared,
                    ReplacementResidualSeverity.High,
                    "Shared source ownership defers the whole candidate.");
            }
        }

        foreach (var candidate in uniqueCandidates.Where(candidate => deferred.Contains(candidate.CandidateId)))
        {
            foreach (var sourceId in candidate.Claims.Select(claim => claim.SourceId).Where(id => !string.IsNullOrWhiteSpace(id)))
                preserved.Add(sourceId);
        }

        foreach (var pair in sourceGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var sourceId = pair.Key;
            if (string.IsNullOrWhiteSpace(sourceId) || invalidSourceIds.Contains(sourceId))
            {
                preserved.Add(sourceId);
                continue;
            }

            if (!claimsBySource.TryGetValue(sourceId, out var sourceClaims) || sourceClaims.Length == 0)
            {
                preserved.Add(sourceId);
                continue;
            }

            var active = sourceClaims
                .Where(item => !deferred.Contains(item.candidate.CandidateId))
                .ToArray();
            var activeCandidateIds = active
                .Select(item => item.candidate.CandidateId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (activeCandidateIds.Length != 1)
            {
                preserved.Add(sourceId);
                continue;
            }

            if (active.Any(item => item.claim.Role == SourceUsageRole.HatchBoundary))
            {
                preserved.Add(sourceId);
                continue;
            }

            if (!active.Any(item => IsSuppressible(item.claim.Role)))
            {
                preserved.Add(sourceId);
                continue;
            }

            eligible.Add(sourceId);
            coverage[sourceId] = activeCandidateIds;
        }

        preserved.ExceptWith(eligible);

        return new SourceReplacementPlan(
            eligible.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            preserved.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            deferred.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            conflicts
                .OrderBy(conflict => conflict.SourceId, StringComparer.Ordinal)
                .ThenBy(conflict => conflict.Reason)
                .ToArray(),
            residuals
                .OrderBy(residual => residual.SourceId, StringComparer.Ordinal)
                .ThenBy(residual => residual.CandidateKey, StringComparer.Ordinal)
                .ThenBy(residual => residual.Kind)
                .ToArray());
    }

    private static bool IsSuppressible(SourceUsageRole role)
        => role is SourceUsageRole.DimensionLine
            or SourceUsageRole.ExtensionLine
            or SourceUsageRole.ArrowGeometry
            or SourceUsageRole.LeaderShaft
            or SourceUsageRole.LeaderArrow
            or SourceUsageRole.LeaderLanding
            or SourceUsageRole.AxisGeometry
            or SourceUsageRole.LevelMarker
            or SourceUsageRole.Text
            or SourceUsageRole.HatchPattern;

    private static IEnumerable<string> ClaimSourceIds(IEnumerable<RecognizerSourceClaim> claims)
        => claims.Select(claim => claim.SourceId);

    private static IReadOnlyList<string> NormalizeDeclaredSourceIds(IEnumerable<string> sourceIds)
        => sourceIds
            .Select(sourceId =>
            {
                if (string.IsNullOrWhiteSpace(sourceId)) return string.Empty;
                var marker = sourceId.IndexOf('#');
                return marker < 0 ? sourceId : sourceId[..marker];
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RecognizerSourceClaim> NormalizeClaims(
        IEnumerable<RecognizerSourceClaim> claims)
        => claims
            .Distinct()
            .OrderBy(claim => claim.SourceId, StringComparer.Ordinal)
            .ThenBy(claim => claim.Role)
            .ThenBy(claim => claim.State)
            .ThenBy(claim => claim.IsPartial)
            .ToArray();

    private static bool TryGetRoleContract(
        string semanticType,
        out SemanticRoleContract contract)
    {
        contract = semanticType switch
        {
            "DIMENSION" => new(
                [SourceUsageRole.DimensionLine, SourceUsageRole.ExtensionLine, SourceUsageRole.ArrowGeometry, SourceUsageRole.Text],
                [SourceUsageRole.DimensionLine, SourceUsageRole.ExtensionLine, SourceUsageRole.ArrowGeometry, SourceUsageRole.Text]),
            "LEADER" => new(
                [SourceUsageRole.LeaderShaft, SourceUsageRole.LeaderArrow, SourceUsageRole.Text],
                [SourceUsageRole.LeaderShaft, SourceUsageRole.LeaderArrow, SourceUsageRole.LeaderLanding, SourceUsageRole.Text]),
            "AXIS" => new(
                [SourceUsageRole.AxisGeometry],
                [SourceUsageRole.AxisGeometry]),
            "LEVEL" => new(
                [SourceUsageRole.LevelMarker, SourceUsageRole.Text],
                [SourceUsageRole.LevelMarker, SourceUsageRole.Text]),
            "ARC_DIMENSION" => new(
                [SourceUsageRole.DimensionLine, SourceUsageRole.ArrowGeometry, SourceUsageRole.Text],
                [SourceUsageRole.DimensionLine, SourceUsageRole.ArrowGeometry, SourceUsageRole.Text]),
            "HATCH" => new(
                [SourceUsageRole.HatchBoundary, SourceUsageRole.HatchPattern],
                [SourceUsageRole.HatchBoundary, SourceUsageRole.HatchPattern]),
            _ => null!
        };

        return contract is not null;
    }

    private static bool EquivalentCandidateDescriptor(CandidateDescriptor first, CandidateDescriptor second)
        => string.Equals(first.SemanticType, second.SemanticType, StringComparison.Ordinal)
            && first.HatchClassification == second.HatchClassification
            && first.DeclaredSourceIds.SequenceEqual(second.DeclaredSourceIds, StringComparer.Ordinal)
            && first.Claims
                .OrderBy(claim => claim.SourceId, StringComparer.Ordinal)
                .ThenBy(claim => claim.Role)
                .ThenBy(claim => claim.State)
                .ThenBy(claim => claim.IsPartial)
                .SequenceEqual(second.Claims
                    .OrderBy(claim => claim.SourceId, StringComparer.Ordinal)
                    .ThenBy(claim => claim.Role)
                    .ThenBy(claim => claim.State)
                    .ThenBy(claim => claim.IsPartial));

    private static void AddWarningEvidence(
        ISet<string> evidenceOnlySourceIds,
        IEnumerable<SemanticWarning> warnings)
    {
        foreach (var sourceId in warnings
                     .SelectMany(warning => warning.ProvenanceIds)
                     .SelectMany(sourceId => NormalizeDeclaredSourceIds([sourceId])))
        {
            evidenceOnlySourceIds.Add(sourceId);
        }
    }

    private static void AddWarningResiduals(
        ICollection<ReplacementResidual> residuals,
        IEnumerable<SemanticWarning> warnings,
        string scope)
    {
        foreach (var warning in warnings)
        {
            AddResidual(
                residuals,
                "(page)",
                null,
                ReplacementResidualKind.Unrecognized,
                ReplacementResidualSeverity.Medium,
                $"{scope} warning {warning.Code}: {warning.Message}");
        }
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

    private static string Fingerprint(DimensionCandidate candidate)
        => string.Join("|",
            candidate.Kind,
            Point(candidate.DefinitionPoint1),
            Point(candidate.DefinitionPoint2),
            Point(candidate.DimensionLinePoint),
            Number(candidate.DisplayedValue),
            Number(candidate.RotationRadians ?? 0d));

    private static string Fingerprint(LeaderCandidate candidate)
        => string.Join("|", Point(candidate.ArrowPoint), Point(candidate.TextPoint), candidate.Text);

    private static string Fingerprint(AxisCandidate candidate)
        => string.Join("|", Point(candidate.Start), Point(candidate.End));

    private static string Fingerprint(LevelCandidate candidate)
        => string.Join("|", Point(candidate.MarkerPoint), Point(candidate.TextPoint), candidate.Value);

    private static string Fingerprint(ArcDimensionCandidate candidate)
        => string.Join("|",
            Point(candidate.Center),
            Number(candidate.Radius),
            Number(candidate.StartAngleRadians),
            Number(candidate.EndAngleRadians),
            Point(candidate.TextPoint),
            candidate.SourceText);

    private static string Fingerprint(HatchCandidate candidate)
        => string.Join(";",
            candidate.Boundary.Select(Point))
            + "|" + Number(candidate.PatternAngleRadians ?? 0d)
            + "|" + Number(candidate.PatternSpacingMillimetres ?? 0d);

    private static string Point(Point2 point)
        => Number(point.X) + "," + Number(point.Y);

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record SemanticRoleContract(
        SourceUsageRole[] Required,
        SourceUsageRole[] Allowed);

    private sealed record CandidateDescriptor(
        string CandidateId,
        string SemanticType,
        IReadOnlyList<RecognizerSourceClaim> Claims,
        IReadOnlyList<string> DeclaredSourceIds,
        HatchClassification? HatchClassification = null);
}
