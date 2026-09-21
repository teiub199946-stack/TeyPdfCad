using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
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

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);
}
