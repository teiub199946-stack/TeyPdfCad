using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
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

        var plan = new SourceReplacementPlanner().BuildPlan(sources, EmptySemantics());

        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Empty(plan.SuppressedSourceIds);
        Assert.True(plan.IsFullPassEligible);
    }

    [Fact]
    public void Valid_whole_level_claim_suppresses_line_and_text()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(5, 0), new VectorStyle()),
            new VectorText("text", "+3.600", new(6, 0), 2.5, new VectorStyle())
        };
        var semantics = EmptySemantics() with
        {
            Levels = [new LevelCandidate(new(0, 0), new(6, 0), "+3.600", 0.95, ["line", "text"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics);

        Assert.Equal(new[] { "line", "text" }, plan.SuppressedSourceIds.OrderBy(x => x));
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Unresolved_warning_blocks_suppression_for_same_source()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(20, 0), new VectorStyle())
        };
        var semantics = EmptySemantics() with
        {
            Axes = [new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["line"])],
            Warnings = [new SemanticWarning("axis-low-confidence", "review", ["line"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics);

        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.DoesNotContain("line", plan.SuppressedSourceIds);
        Assert.Contains(plan.Conflicts, conflict => conflict.Reason == ReplacementConflictReason.UnresolvedClaim);
        Assert.False(plan.IsFullPassEligible);
    }

    [Fact]
    public void Partial_provenance_never_suppresses_whole_polyline()
    {
        var sources = new VectorEntity[]
        {
            new VectorPolyline("poly", [new(0, 0), new(10, 0), new(10, 10)], false, new VectorStyle())
        };
        var semantics = EmptySemantics() with
        {
            Axes = [new AxisCandidate(new(0, 0), new(10, 0), 0.95, ["poly#segment:0"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics);

        Assert.Contains("poly", plan.PreservedSourceIds);
        Assert.Contains(plan.Conflicts, conflict => conflict.Reason == ReplacementConflictReason.PartialProvenance);
    }

    [Fact]
    public void Two_different_native_candidates_on_same_source_are_preserved_as_conflict()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("line", new(0, 0), new(20, 0), new VectorStyle())
        };
        var semantics = EmptySemantics() with
        {
            Axes = [new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["line"])],
            Leaders = [new LeaderCandidate(new(0, 0), new(20, 0), "К-1", 0.95, ["line"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics);

        Assert.Contains("line", plan.PreservedSourceIds);
        Assert.Contains(plan.Conflicts, conflict => conflict.Reason == ReplacementConflictReason.MultipleCandidates);
    }

    [Fact]
    public void Pattern_hatch_suppresses_pattern_lines_but_preserves_boundary()
    {
        var boundary = new VectorPolyline(
            "boundary", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true, new VectorStyle());
        var sources = new VectorEntity[]
        {
            boundary,
            new VectorLine("h1", new(1, 4), new(19, 4), new VectorStyle()),
            new VectorLine("h2", new(1, 8), new(19, 8), new VectorStyle()),
            new VectorLine("h3", new(1, 12), new(19, 12), new VectorStyle())
        };
        var hatch = new HatchCandidate(
            boundary.Vertices, false, 0, 4, new VectorStyle(), 0.95, ["boundary", "h1", "h2", "h3"]);
        var hatchResult = new HatchRecognitionResult([hatch], []);

        var plan = new SourceReplacementPlanner().BuildPlan(sources, EmptySemantics(), hatchResult);

        Assert.Contains("boundary", plan.PreservedSourceIds);
        Assert.DoesNotContain("boundary", plan.SuppressedSourceIds);
        Assert.Equal(new[] { "h1", "h2", "h3" }, plan.SuppressedSourceIds.OrderBy(x => x));
        Assert.True(plan.IsFullPassEligible);
    }

    [Fact]
    public void Protected_hatch_boundary_overlapping_semantic_candidate_is_preserved_and_reported()
    {
        var boundary = new VectorPolyline(
            "boundary", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true, new VectorStyle());
        var sources = new VectorEntity[] { boundary };
        var semantics = EmptySemantics() with
        {
            Axes = [new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["boundary"])]
        };
        var hatch = new HatchCandidate(
            boundary.Vertices, false, 0, 4, new VectorStyle(), 0.95, ["boundary"]);
        var hatchResult = new HatchRecognitionResult([hatch], []);

        var plan = new SourceReplacementPlanner().BuildPlan(sources, semantics, hatchResult);

        Assert.Contains("boundary", plan.PreservedSourceIds);
        Assert.Contains(plan.Conflicts, conflict => conflict.Reason == ReplacementConflictReason.ProtectedRoleOverlap);
    }

    [Fact]
    public void Missing_source_claim_is_reported_deterministically()
    {
        var semantics = EmptySemantics() with
        {
            Axes = [new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["missing"])]
        };

        var plan = new SourceReplacementPlanner().BuildPlan([], semantics);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("missing", conflict.SourceId);
        Assert.Equal(ReplacementConflictReason.SourceMissingFromPage, conflict.Reason);
        Assert.False(plan.IsFullPassEligible);
    }

    [Fact]
    public void Candidate_order_does_not_change_source_decisions()
    {
        var sources = new VectorEntity[]
        {
            new VectorLine("a", new(0, 0), new(20, 0), new VectorStyle()),
            new VectorLine("b", new(0, 10), new(20, 10), new VectorStyle())
        };
        var first = EmptySemantics() with
        {
            Axes =
            [
                new AxisCandidate(new(0, 0), new(20, 0), 0.95, ["a"]),
                new AxisCandidate(new(0, 10), new(20, 10), 0.95, ["b"])
            ]
        };
        var second = first with { Axes = first.Axes.Reverse().ToArray() };

        var planner = new SourceReplacementPlanner();
        var plan1 = planner.BuildPlan(sources, first);
        var plan2 = planner.BuildPlan(sources, second);

        Assert.Equal(plan1.SuppressedSourceIds, plan2.SuppressedSourceIds);
        Assert.Equal(plan1.PreservedSourceIds, plan2.PreservedSourceIds);
        Assert.Equal(plan1.Conflicts, plan2.Conflicts);
    }

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);
}
