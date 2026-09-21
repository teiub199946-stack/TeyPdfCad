using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class SourceEquivalenceAssessorTests
{
    [Fact]
    public void Axis_is_fail_closed_when_source_appearance_is_not_fully_represented()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine(
                "axis",
                new Point2(0, 0),
                new Point2(25, 0),
                new VectorStyle(
                    StrokeWidthPoints: 0.5,
                    DashPatternPoints: [10, 2, 2, 2]))
        ]);
        var axis = new AxisCandidate(
            new Point2(0, 0),
            new Point2(25, 0),
            0.95,
            ["axis"])
        {
            SourceClaims =
            [
                new(
                    "axis",
                    SourceUsageRole.AxisGeometry,
                    SourceClaimState.Valid,
                    false)
            ]
        };
        var semantics = EmptySemantics() with { Axes = [axis] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var id = SourceReplacementPlanner.GetCandidateKey(axis, 1);
        var assessment = result.GetRequired(id);

        Assert.False(assessment.IsComplete);
        Assert.Equal("AXIS", assessment.SemanticType);
        Assert.Contains("dash", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lineweight", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_current_destructive_semantic_type_is_incomplete_until_source_appearance_contract_exists()
    {
        var dimension = new DimensionCandidate(
            DimensionKind.Aligned,
            new(0, 0),
            new(10, 0),
            new(0, 5),
            10,
            10,
            1,
            0.95,
            "10",
            1,
            ["d-line", "d-ext", "d-text"])
        {
            SourceClaims =
            [
                new("d-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("d-ext", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var leader = new LeaderCandidate(
            new(0, 10),
            new(10, 10),
            "K-1",
            0.95,
            ["l-shaft", "l-arrow", "l-text"])
        {
            SourceClaims =
            [
                new("l-shaft", SourceUsageRole.LeaderShaft, SourceClaimState.Valid, false),
                new("l-arrow", SourceUsageRole.LeaderArrow, SourceClaimState.Valid, false),
                new("l-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var level = new LevelCandidate(
            new(0, 20),
            new(10, 20),
            "+3.600",
            0.95,
            ["level-marker", "level-text"])
        {
            SourceClaims =
            [
                new("level-marker", SourceUsageRole.LevelMarker, SourceClaimState.Valid, false),
                new("level-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var arc = new ArcDimensionCandidate(
            new(0, 30),
            10,
            0,
            Math.PI / 2,
            new(8, 38),
            "R10",
            0.95,
            ["arc-line", "arc-text"])
        {
            SourceClaims =
            [
                new("arc-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("arc-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var axis = new AxisCandidate(
            new(0, 40),
            new(20, 40),
            0.95,
            ["axis"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var page = new VectorPdfPage(
            1,
            100,
            100,
            0,
            [
                new VectorLine("d-line", new(0, 0), new(10, 0), new VectorStyle()),
                new VectorLine("d-ext", new(0, 0), new(0, 5), new VectorStyle()),
                new VectorText("d-text", "10", new(5, 5), 2.5, new VectorStyle()),
                new VectorLine("l-shaft", new(0, 10), new(10, 10), new VectorStyle()),
                new VectorLine("l-arrow", new(0, 10), new(1, 11), new VectorStyle()),
                new VectorText("l-text", "K-1", new(10, 10), 2.5, new VectorStyle()),
                new VectorPolyline(
                    "level-marker",
                    [new(0, 20), new(2, 21), new(2, 19)],
                    true,
                    new VectorStyle()),
                new VectorText("level-text", "+3.600", new(10, 20), 2.5, new VectorStyle()),
                new VectorLine("arc-line", new(0, 30), new(10, 30), new VectorStyle()),
                new VectorText("arc-text", "R10", new(8, 38), 2.5, new VectorStyle()),
                new VectorLine("axis", new(0, 40), new(20, 40), new VectorStyle())
            ]);
        var semantics = EmptySemantics() with
        {
            Dimensions = [dimension],
            Leaders = [leader],
            Levels = [level],
            ArcDimensions = [arc],
            Axes = [axis]
        };

        var assessments = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        Assert.Equal(5, assessments.Candidates.Count);
        Assert.All(
            assessments.Candidates.Values,
            assessment => Assert.False(assessment.IsComplete));
    }

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);
}
