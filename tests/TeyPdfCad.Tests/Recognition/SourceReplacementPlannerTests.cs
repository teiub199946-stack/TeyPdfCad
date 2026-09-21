using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class SourceReplacementPlannerTests
{
    [Fact]
    public void Source_without_claims_is_preserved()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(10, 0), new VectorStyle())
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, EmptySemantics(), pageNumber: 1);

        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Empty(plan.EligibleSourceIds);
        Assert.True(plan.IsFullPassEligible);
    }

    [Fact]
    public void Valid_explicit_level_claims_make_sources_eligible_but_not_authorized()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(5, 0), new VectorStyle()),
            new VectorText("text", "+3.600", new(6, 0), 2.5, new VectorStyle())
        };
        var level = new LevelCandidate(new(0, 0), new(6, 0), "+3.600", 0.95, ["line", "text"])
        {
            SourceClaims =
            [
                new("line", SourceUsageRole.LevelMarker, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with { Levels = [level] };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics, pageNumber: 1);

        Assert.Equal(new[] { "line", "text" }, plan.EligibleSourceIds.OrderBy(x => x));
        Assert.Empty(plan.Conflicts);
        Assert.Empty(new SuppressionGate()
            .Evaluate(plan, new NativeReadBackVerification(
                new Dictionary<string, CandidateVerification>(StringComparer.Ordinal)))
            .SuppressSourceIds);
    }

    [Fact]
    public void Unresolved_claim_defers_candidate_and_preserves_source()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(20, 0), new VectorStyle())
        };
        var axis = new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["line"])
        {
            SourceClaims =
            [
                new("line", SourceUsageRole.AxisGeometry, SourceClaimState.Unresolved, false)
            ]
        };
        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);

        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims
            && residual.Severity == ReplacementResidualSeverity.High);
    }

    [Fact]
    public void Partial_claim_never_suppresses_whole_source()
    {
        var sources = new VectorEntity[]
        {
            new VectorPolyline("poly", [new(0, 0), new(10, 0), new(10, 10)], false, new VectorStyle())
        };
        var axis = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["poly#segment:0"])
        {
            SourceClaims =
            [
                new("poly", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, true)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);

        Assert.Contains("poly", plan.PreservedSourceIds);
        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims);
    }

    [Fact]
    public void Planner_does_not_infer_role_from_vector_entity_runtime_type()
    {
        var sources = new VectorEntity[]
        {
            new VectorText("text", "A", new(0, 0), 2.5, new VectorStyle())
        };
        var axis = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["text"])
        {
            SourceClaims =
            [
                new("text", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);

        Assert.Contains("text", plan.EligibleSourceIds);
        Assert.DoesNotContain(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims);
    }

    [Fact]
    public void Missing_explicit_claim_set_defers_candidate()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(20, 0), new VectorStyle())
        };
        var axis = new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["line"]);

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims);
    }

    [Fact]
    public void Two_candidates_on_same_source_are_deferred_as_shared()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("shared", new(0, 0), new(20, 0), new VectorStyle()),
            new VectorLine("a-only", new(0, 1), new(20, 1), new VectorStyle()),
            new VectorLine("b-only", new(0, 2), new(20, 2), new VectorStyle())
        };
        var axis = new AxisCandidate(new(0, 0), new(20, 1), 0.95, ["shared", "a-only"])
        {
            SourceClaims =
            [
                new("shared", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("a-only", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var leader = new LeaderCandidate(new(0, 0), new(20, 2), "K-1", 0.95, ["shared", "b-only"])
        {
            SourceClaims =
            [
                new("shared", SourceUsageRole.LeaderShaft, SourceClaimState.Valid, false),
                new("b-only", SourceUsageRole.LeaderShaft, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis], Leaders = [leader] },
            pageNumber: 1);

        Assert.Equal(2, plan.DeferredCandidateKeys.Count);
        Assert.Empty(plan.EligibleSourceIds);
        Assert.Equal(new[] { "a-only", "b-only", "shared" }, plan.PreservedSourceIds.OrderBy(x => x));
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredShared
            && residual.Severity == ReplacementResidualSeverity.High);
    }

    [Fact]
    public void Candidate_order_does_not_change_shared_deferral()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("shared", new(0, 0), new(20, 0), new VectorStyle())
        };
        var firstAxis = new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["shared"])
        {
            SourceClaims = [new("shared", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)]
        };
        var secondAxis = new AxisCandidate(new(0, 0), new(30, 0), 0.96, ["shared"])
        {
            SourceClaims = [new("shared", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)]
        };

        var first = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [firstAxis, secondAxis] },
            pageNumber: 1);
        var second = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [secondAxis, firstAxis] },
            pageNumber: 1);

        Assert.Equal(first.DeferredCandidateKeys, second.DeferredCandidateKeys);
        Assert.Equal(first.PreservedSourceIds, second.PreservedSourceIds);
        Assert.Equal(first.EligibleSourceIds, second.EligibleSourceIds);
    }

    [Fact]
    public void Explicit_confident_hatch_preserves_boundary_and_marks_only_pattern_eligible()
    {
        var boundary = new VectorPolyline(
            "boundary", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true, new VectorStyle("ШТРИХОВКА"));
        var sources = new VectorEntity[]
        {
            boundary,
            new VectorLine("h1", new(1, 4), new(19, 4), new VectorStyle()),
            new VectorLine("h2", new(1, 8), new(19, 8), new VectorStyle()),
            new VectorLine("h3", new(1, 12), new(19, 12), new VectorStyle())
        };
        var hatch = new HatchCandidate(
            boundary.Vertices, false, 0, 4, boundary.Style, 0.95, ["boundary", "h1", "h2", "h3"]);
        var candidateId = SourceReplacementPlanner.GetCandidateKey(hatch, 1);
        var hatchResult = new HatchRecognitionResult(
            [hatch],
            [],
            [new HatchClaim(candidateId, ["boundary"], ["h1", "h2", "h3"], HatchClassification.Confident)]);

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics(),
            hatchResult,
            pageNumber: 1);

        Assert.Contains("boundary", plan.PreservedSourceIds);
        Assert.Equal(new[] { "h1", "h2", "h3" }, plan.EligibleSourceIds.OrderBy(x => x));
        Assert.True(plan.IsFullPassEligible);
    }

    [Fact]
    public void Uncertain_hatch_emits_no_eligibility_and_preserves_all_sources()
    {
        var boundary = new VectorPolyline(
            "boundary", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true, new VectorStyle());
        var sources = new VectorEntity[]
        {
            boundary,
            new VectorLine("h1", new(1, 4), new(19, 4), new VectorStyle())
        };
        var candidateId = SourceReplacementPlanner.CreateCandidateId(
            1, "HATCH", ["boundary", "h1"], "fixture");
        var hatchResult = new HatchRecognitionResult(
            [],
            [],
            [new HatchClaim(candidateId, ["boundary"], ["h1"], HatchClassification.Uncertain)]);

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics(),
            hatchResult,
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Equal(new[] { "boundary", "h1" }, plan.PreservedSourceIds.OrderBy(x => x));
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUncertainHatch
            && residual.Severity == ReplacementResidualSeverity.Medium);
    }

    [Fact]
    public void Missing_source_claim_is_reported_deterministically()
    {
        var axis = new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["missing"])
        {
            SourceClaims = [new("missing", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            [],
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);

        Assert.Contains(plan.Conflicts, conflict =>
            conflict.SourceId == "missing"
            && conflict.Reason == ReplacementConflictReason.SourceMissingFromPage);
        Assert.False(plan.IsFullPassEligible);
    }

    [Fact]
    public void Verified_candidate_is_the_only_path_from_eligibility_to_suppression()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(5, 0), new VectorStyle())
        };
        var axis = new AxisCandidate(new(0, 0), new(5, 0), 0.95, ["line"])
        {
            SourceClaims = [new("line", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)]
        };
        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [axis] },
            pageNumber: 1);
        var candidateId = Assert.Single(plan.SourceCoverageMap["line"]);

        var rejected = new SuppressionGate().Evaluate(
            plan,
            new NativeReadBackVerification(
                new Dictionary<string, CandidateVerification>(StringComparer.Ordinal)));
        Assert.Empty(rejected.SuppressSourceIds);
        Assert.Contains(rejected.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.CandidateNotVerified
            && residual.Severity == ReplacementResidualSeverity.Critical);

        var accepted = new SuppressionGate().Evaluate(
            plan,
            new NativeReadBackVerification(
                new Dictionary<string, CandidateVerification>(StringComparer.Ordinal)
                {
                    [candidateId] = new(candidateId, true, [], [], [])
                    {
                        SourceEquivalenceComplete = true
                    }
                }));
        Assert.Equal(new[] { "line" }, accepted.SuppressSourceIds.OrderBy(x => x));
    }


    [Fact]
    public void ChainDeferral_DeferredCandidateStillCountsForNextSource()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("s1", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorLine("a", new(0, 1), new(10, 1), new VectorStyle()),
            new VectorLine("s2", new(0, 2), new(10, 2), new VectorStyle()),
            new VectorLine("b", new(0, 3), new(10, 3), new VectorStyle()),
            new VectorLine("s3", new(0, 4), new(10, 4), new VectorStyle())
        };
        var first = new AxisCandidate(new(0, 0), new(10, 1), 0.95, ["s1", "a"])
        {
            SourceClaims =
            [
                new("s1", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("a", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var middle = new AxisCandidate(new(0, 0), new(10, 3), 0.96, ["s1", "s2", "b"])
        {
            SourceClaims =
            [
                new("s1", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("s2", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("b", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var last = new AxisCandidate(new(0, 2), new(10, 4), 0.97, ["s2", "s3"])
        {
            SourceClaims =
            [
                new("s2", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("s3", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [first, middle, last] },
            pageNumber: 1);

        Assert.Equal(3, plan.DeferredCandidateKeys.Count);
        Assert.Empty(plan.EligibleSourceIds);
        Assert.Equal(new[] { "a", "b", "s1", "s2", "s3" }, plan.PreservedSourceIds.OrderBy(x => x));
        Assert.Contains(plan.Residuals, residual =>
            residual.SourceId == "s2"
            && residual.Kind == ReplacementResidualKind.DeferredShared);
    }

    [Fact]
    public void SameSource_SameRoleTwice_IsDeduplicatedExplicitly()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("axis", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["axis"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Equal(new[] { "axis" }, plan.EligibleSourceIds);
        Assert.DoesNotContain(plan.Conflicts, conflict =>
            conflict.Reason == ReplacementConflictReason.ClaimRoleConflict);
    }

    [Fact]
    public void UnexpectedRoleForSemanticType_Rejected()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("axis", new(0, 0), new(10, 0), new VectorStyle()),
            new VectorText("text", "A", new(5, 0), 2.5, new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["axis", "text"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Equal(new[] { "axis", "text" }, plan.PreservedSourceIds.OrderBy(x => x));
        Assert.Contains(plan.Residuals, residual =>
            residual.Kind == ReplacementResidualKind.DeferredUnresolvedClaims
            && residual.Severity == ReplacementResidualSeverity.High);
    }

    [Fact]
    public void PartialSourceIdFormat_RequiresFailClosedHandling()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("A1#segment:0", new(0, 0), new(10, 0), new VectorStyle())
        };
        var candidate = new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["A1#segment:0"])
        {
            SourceClaims =
            [
                new("A1#segment:0", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            EmptySemantics() with { Axes = [candidate] },
            pageNumber: 1);

        Assert.Empty(plan.EligibleSourceIds);
        Assert.Contains("A1#segment:0", plan.PreservedSourceIds);
    }

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);
}
