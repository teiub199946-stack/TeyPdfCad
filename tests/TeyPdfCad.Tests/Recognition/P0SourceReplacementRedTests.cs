using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class P0SourceReplacementRedTests
{
    [Fact]
    public void Canonical_candidate_id_is_stable_when_source_order_changes()
    {
        var first = SourceReplacementPlanner.CreateCandidateId(
            29, "LEADER", ["s-2", "s-1"], "normalized-geometry");
        var second = SourceReplacementPlanner.CreateCandidateId(
            29, "LEADER", ["s-1", "s-2"], "normalized-geometry");

        Assert.Equal(first, second);
        Assert.StartsWith("v1:29:LEADER:", first);
    }

    [Fact]
    public void Control_character_source_id_is_not_suppressible()
    {
        const string invalidSourceId = "source\nline";
        var sources = new VectorEntity[]
        {
            new VectorLine(
                invalidSourceId,
                new(0, 0),
                new(10, 0),
                new VectorStyle())
        };
        var axis = new AxisCandidate(
            new(0, 0),
            new(10, 0),
            0.95,
            [invalidSourceId])
        {
            SourceClaims =
            [
                new(
                    invalidSourceId,
                    SourceUsageRole.AxisGeometry,
                    SourceClaimState.Valid,
                    false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(invalidSourceId, plan.PreservedSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.SourceId == invalidSourceId
            && residual.Kind == ReplacementResidualKind.SourceIdentityViolation
            && residual.Severity == ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public void Duplicate_source_id_is_never_collapsed()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("dup", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("dup", new(0, 1), new(10, 1), new VectorStyle())
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, EmptySemantics());

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("dup", plan.PreservedSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.SourceIdentityViolation
            && residual.Severity == ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public void Empty_source_id_is_not_suppressible()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine(string.Empty, new(0, 0), new(10, 0), new VectorStyle())
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, EmptySemantics());

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.SourceIdentityViolation
            && residual.Severity == ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public void Duplicate_source_id_for_verified_candidate_still_preserves_sources()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("dup", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("dup", new(0, 1), new(10, 1), new VectorStyle()),
            new VectorText("text", "+3.600", new(11, 0), 2.5, new VectorStyle())
        };
        var level = new LevelCandidate(new(0, 0), new(11, 0), "+3.600", 0.95, ["dup", "text"])
        {
            SourceClaims =
            [
                new RecognizerSourceClaim("dup", SourceUsageRole.LevelMarker, SourceClaimState.Valid, false),
                new RecognizerSourceClaim("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with { Levels = [level] };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics, pageNumber: 1);
        var candidateId = SourceReplacementPlanner.CreateCandidateId(
            1, "LEVEL", ["dup", "text"], "0,0|11,0|+3.600");
        var verification = new NativeReadBackVerification(
            new Dictionary<string, CandidateVerification>(StringComparer.Ordinal)
            {
                [candidateId] = new(candidateId, true, [], [], [])
            });

        var decision = new SuppressionGate().Evaluate(plan, verification);

        Assert.Empty(decision.SuppressSourceIds);
        Assert.Contains("dup", decision.PreserveSourceIds);
        Assert.Contains(decision.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.SourceIdentityViolation
            && residual.Severity == ReplacementResidualSeverity.Critical);
    }


    [Fact]
    public void Dimension_MissingExtensionLine_IsDeferred_NoEligibleSources()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("dim", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new DimensionCandidate(
            DimensionKind.Rotated,
            new(0, 0),
            new(10, 0),
            new(5, 0),
            10,
            10,
            1,
            0.95,
            "10",
            1,
            ["dim"])
        {
            SourceClaims =
            [
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Dimensions = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("dim", plan.PreservedSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims
            && residual.Severity == ReplacementResidualSeverity.High);
    }


    [Fact]
    public void Dimension_MissingArrowGeometry_IsDeferred_NoEligibleSources()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("dim", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("ext", new(0, 0), new(0, 5), new VectorStyle()),
            new VectorText("text", "10", new(5, 2), 2.5, new VectorStyle())
        };
        var candidate = new DimensionCandidate(
            DimensionKind.Rotated,
            new(0, 0),
            new(10, 0),
            new(5, 2),
            10,
            10,
            1,
            0.95,
            "10",
            0,
            ["dim", "ext", "text"])
        {
            SourceClaims =
            [
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("ext", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Dimensions = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Equal(
            new[] { "dim", "ext", "text" },
            plan.PreservedSourceIds.OrderBy(value => value));
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims
            && residual.Severity == ReplacementResidualSeverity.High);
    }

    [Fact]
    public void SameSource_TwoRoles_SameCandidate_Violation()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("same", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["same"])
        {
            SourceClaims =
            [
                new("same", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("same", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("same", plan.PreservedSourceIds);
        Assert.Contains(plan.Conflicts, conflict =>
            conflict.Reason == ReplacementConflictReason.ClaimRoleConflict);
    }

    [Fact]
    public void CandidateSourceSet_MustEqualUnionOfClaims_MismatchDefers()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("a", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("b", new(0, 1), new(10, 1), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["a", "b"])
        {
            SourceClaims =
            [
                new("a", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Equal(new[] { "a", "b" }, plan.PreservedSourceIds.OrderBy(x => x));
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims);
    }

    [Fact]
    public void Claim_UnsetRole_Rejected_NotSilentlyDefaulted()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["line"])
        {
            SourceClaims =
            [
                new("line", default, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims);
    }

    [Fact]
    public void PartialFlag_AlwaysDefers_RegardlessOfSourceShape()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("whole-id", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["whole-id"])
        {
            SourceClaims =
            [
                new("whole-id", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, true)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("whole-id", plan.PreservedSourceIds);
    }

    [Fact]
    public void TwoCandidates_SameCandidateId_WithDifferentClaims_BothDeferred()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(10, 0), new VectorStyle())
        };
        var first = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["line"])
        {
            SourceClaims =
            [
                new("line", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var second = first with
        {
            SourceClaims =
            [
                new("line", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [first, second] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.CandidateIdentityViolation
            && residual.Severity == ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public void DuplicateCandidate_ExactDuplicate_IsDeduplicatedDeterministically()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["line"])
        {
            SourceClaims =
            [
                new("line", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate, candidate] },
            pageNumber: 1);

        Assert.Equal(new[] { "line" }, plan.EligibleSourceIds.OrderBy(x => x));
        Assert.DoesNotContain(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.CandidateIdentityViolation);
    }

    [Fact]
    public void Hatch_BoundaryPatternOverlap_Rejected()
    {
        var source = new VectorPolyline(
            "same",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            true,
            new VectorStyle("ШТРИХОВКА"));
        var candidateId = SourceReplacementPlanner.CreateCandidateId(
            1,
            "HATCH",
            ["same"],
            "fixture");
        var hatch = new HatchRecognitionResult(
            [],
            [],
            [new HatchClaim(candidateId, ["same"], ["same"], HatchClassification.Confident)]);

        var plan = new SourceReplacementPlanner().BuildPlan(
            [source],
            EmptySemantics(),
            hatch,
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("same", plan.PreservedSourceIds);
        Assert.Contains(plan.Conflicts, conflict =>
            conflict.Reason == ReplacementConflictReason.ClaimRoleConflict);
    }

    [Fact]
    public void Claim_UnknownSourceId_Defers()
    {
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["missing"])
        {
            SourceClaims =
            [
                new("missing", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            [],
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Conflicts, conflict =>
            conflict.Reason == ReplacementConflictReason.SourceMissingFromPage);
    }

    [Fact]
    public void EmptyCandidate_Rejected()
    {
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, []);

        var plan = new SourceReplacementPlanner().BuildPlan(
            [],
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims);
    }

    [Fact]
    public void TwoEntities_EmptySourceId_AreNeverEligible()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine(string.Empty, new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine(string.Empty, new(0, 1), new(10, 1), new VectorStyle())
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, EmptySemantics(), pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.SourceIdentityViolation
            && residual.Detail.Contains("2 source object", StringComparison.Ordinal));
    }


    [Fact]
    public void WarningEvidence_OverlappingCandidate_BlocksEligibility()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["line"])
        {
            SourceClaims =
            [
                new("line", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with
        {
            Axes = [candidate],
            Warnings = [new SemanticWarning("review", "ambiguous", ["line"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics, pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Contains(plan.Conflicts, conflict =>
            conflict.Reason == ReplacementConflictReason.UnresolvedClaim);
    }

    [Fact]
    public void OrphanEvidenceOnlyClaim_SourcePreserved_NoCandidateImpact()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("evidence", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("axis", new(0, 1), new(10, 1), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 1), new(10, 1), 0.95, ["axis"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with
        {
            Axes = [candidate],
            Warnings = [new SemanticWarning("review", "orphan evidence", ["evidence"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics, pageNumber: 1);

        Assert.Contains("evidence", plan.PreservedSourceIds);
        Assert.Contains("axis", plan.EligibleSourceIds);
        Assert.DoesNotContain(plan.DeferredCandidateKeys,
            candidateId => candidateId == SourceReplacementPlanner.GetCandidateKey(candidate, 1));
    }

    [Fact]
    public void WarningEvidence_UnknownSource_IsCriticalAndNeverEligible()
    {
        var semantics = EmptySemantics() with
        {
            Warnings = [new SemanticWarning("bad-provenance", "missing", ["missing"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan([], semantics, pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Conflicts, conflict =>
            conflict.SourceId == "missing"
            && conflict.Reason == ReplacementConflictReason.SourceMissingFromPage);
        Assert.Contains(plan.Residuals, residual =>
            residual.SourceId == "missing"
            && residual.Severity == ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public void Adding_safety_blockers_never_restores_dimension_eligibility()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("dim", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("ext", new(0, 0), new(0, 5), new VectorStyle()),
            new VectorLine("arrow", new(-1, -1), new(1, 1), new VectorStyle()),
            new VectorText("text", "10", new(5, 2), 2.5, new VectorStyle())
        };

        DimensionCandidate Candidate(IReadOnlyList<RecognizerSourceClaim> claims)
            => new(
                DimensionKind.Rotated,
                new(0, 0),
                new(10, 0),
                new(5, 2),
                10,
                10,
                1,
                0.95,
                "10",
                1,
                ["dim", "ext", "arrow", "text"])
            {
                SourceClaims = claims
            };

        var cleanClaims = new RecognizerSourceClaim[]
        {
            new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
            new("ext", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
            new("arrow", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
            new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
        };
        var clean = Candidate(cleanClaims);
        var cleanPlan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Dimensions = [clean] },
            pageNumber: 1);

        Assert.NotEmpty(cleanPlan.EligibleSourceIds);

        var partial = Candidate(
        [
            ..cleanClaims.Where(claim => claim.SourceId != "arrow"),
            new("arrow", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, true)
        ]);
        var partialPlan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Dimensions = [partial] },
            pageNumber: 1);
        Assert.Empty(partialPlan.EligibleSourceIds);

        var partialPlusRoleConflict = Candidate(
        [
            ..partial.SourceClaims,
            new("arrow", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false)
        ]);
        var conflictPlan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Dimensions = [partialPlusRoleConflict] },
            pageNumber: 1);
        Assert.Empty(conflictPlan.EligibleSourceIds);

        var warnedSemantics = EmptySemantics() with
        {
            Dimensions = [partialPlusRoleConflict],
            Warnings =
            [
                new SemanticWarning(
                    "dimension-ambiguous",
                    "Competing native interpretation remains unresolved.",
                    ["dim"])
            ]
        };
        var warnedPlan = new SourceReplacementPlanner().BuildPlan(
            sources,
            warnedSemantics,
            pageNumber: 1);

        Assert.Empty(warnedPlan.EligibleSourceIds);
        Assert.Contains("dim", warnedPlan.PreservedSourceIds);
    }

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);
}
