using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class ReplacementExecutionAuditorTests
{
    [Fact]
    public void Suppressed_source_is_safe_when_covering_candidate_was_created()
    {
        var plan = new SourceReplacementPlan(
            ["source"],
            [],
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["source"] = ["candidate"]
            },
            [],
            []);

        var report = ReplacementExecutionAuditor.Build(plan, ["candidate"], readBackConfirmed: true);

        Assert.False(report.AnyGeometryLost);
        Assert.Empty(report.GeometryLostSourceIds);
        Assert.True(report.ReadBackConfirmed);
    }

    [Fact]
    public void Suppressed_source_is_geometry_lost_when_covering_candidate_was_not_created()
    {
        var plan = new SourceReplacementPlan(
            ["source"],
            [],
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["source"] = ["candidate"]
            },
            [],
            []);

        var report = ReplacementExecutionAuditor.Build(plan, [], readBackConfirmed: true);

        Assert.True(report.AnyGeometryLost);
        Assert.Equal(new[] { "source" }, report.GeometryLostSourceIds);
    }

    [Fact]
    public void Suppressed_source_without_coverage_evidence_is_geometry_lost()
    {
        var plan = new SourceReplacementPlan(
            ["source"],
            [],
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            [],
            []);

        var report = ReplacementExecutionAuditor.Build(plan, [], readBackConfirmed: true);

        Assert.True(report.AnyGeometryLost);
        Assert.Contains("source", report.GeometryLostSourceIds);
    }
}
