using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class ReplacementExecutionAuditorTests
{
    [Fact]
    public void Verified_candidate_clears_candidate_not_verified_diagnostic()
    {
        var plan = Plan("source", "candidate");
        var verification = new NativeReadBackVerification(
            new Dictionary<string, CandidateVerification>(StringComparer.Ordinal)
            {
                ["candidate"] = new("candidate", true, [], [], [])
            });

        var report = ReplacementExecutionAuditor.Build(plan, verification);

        Assert.Empty(report.CandidateNotVerifiedSourceIds);
        Assert.False(report.AnyGeometryLost);
        Assert.True(report.ReadBackConfirmed);
    }

    [Fact]
    public void Missing_read_back_evidence_reports_candidate_not_verified_without_geometry_lost()
    {
        var plan = Plan("source", "candidate");
        var verification = new NativeReadBackVerification(
            new Dictionary<string, CandidateVerification>(StringComparer.Ordinal));

        var report = ReplacementExecutionAuditor.Build(plan, verification);

        Assert.Equal(new[] { "source" }, report.CandidateNotVerifiedSourceIds);
        Assert.Empty(report.GeometryLostSourceIds);
        Assert.False(report.AnyGeometryLost);
    }

    [Fact]
    public void Unverified_candidate_reports_candidate_not_verified_without_geometry_lost()
    {
        var plan = Plan("source", "candidate");
        var verification = new NativeReadBackVerification(
            new Dictionary<string, CandidateVerification>(StringComparer.Ordinal)
            {
                ["candidate"] = new("candidate", false, ["annotation"], [], ["missing Text"])
            });

        var report = ReplacementExecutionAuditor.Build(plan, verification);

        Assert.Equal(new[] { "source" }, report.CandidateNotVerifiedSourceIds);
        Assert.Empty(report.GeometryLostSourceIds);
    }

    private static SourceReplacementPlan Plan(string sourceId, string candidateId)
        => new(
            [sourceId],
            [],
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [sourceId] = [candidateId]
            },
            [],
            []);
}
