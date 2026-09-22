using Xunit;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Tests.Recognition;

public sealed class SemanticReconstructionEngineTests
{
    [Fact]
    public void Includes_only_high_confidence_axes_and_leaders_in_reconstruction()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 20), new Point2(120, 20), "ОСИ", ["axis"], DashPatternMm: [12, 3, 2, 3]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(30, 0), SourceIds: ["shaft"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(4, 2), SourceIds: ["arrow-a"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(4, -2), SourceIds: ["arrow-b"]));
        scene.Texts.Add(new TextPrimitive("Позиция 1", new Point2(31, 1), 2.5, 0, SourceIds: ["note"]));

        var result = new SemanticReconstructionEngine().Analyze(scene);

        Assert.Single(result.Axes);
        Assert.Single(result.Leaders);
        Assert.Equal(2, result.ReconstructedObjectCount);
    }

    [Fact]
    public void Publishes_one_primary_dimension_but_retains_all_shared_text_safety_claimants()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, 0, "a", "shared-text");
        AddHorizontalDimension(scene, 30, "b", "shared-text");

        var result = new SemanticReconstructionEngine().Analyze(scene);

        Assert.Single(result.Dimensions);
        Assert.Equal(2, result.DimensionClaimants.Count);
        Assert.All(result.DimensionClaimants, candidate =>
            Assert.Contains(candidate.SourceClaims, claim => claim.SourceId == "shared-text"));
    }

    [Fact]
    public void Reconstructs_Three_Dimension_Chain_And_Dominant_Scale()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(12, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(12, 0), new Point2(30, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(30, 0), new Point2(52, 0)));
        foreach (var x in new[] { 0.0, 12.0, 30.0, 52.0 })
            scene.Lines.Add(new LinePrimitive(new Point2(x, -12), new Point2(x, 1)));
        foreach (var x in new[] { 0.0, 12.0, 30.0, 52.0 })
            scene.Lines.Add(new LinePrimitive(new Point2(x - 1, -1), new Point2(x + 1, 1)));

        scene.Texts.Add(new TextPrimitive("1200", new Point2(6, 3), 2.5, 0));
        scene.Texts.Add(new TextPrimitive("1800", new Point2(21, 3), 2.5, 0));
        scene.Texts.Add(new TextPrimitive("2200", new Point2(41, 3), 2.5, 0));

        var result = new SemanticReconstructionEngine().Analyze(scene);

        Assert.Equal(3, result.Dimensions.Count);
        var chain = Assert.Single(result.DimensionChains);
        Assert.Equal(5200, chain.TotalDisplayedValue, 6);
        Assert.Equal(100, result.DominantDrawingScale);
        var scale = Assert.Single(result.DetectedDrawingScales);
        Assert.Equal(100, scale, 6);
        Assert.True(result.AverageDimensionConfidence >= 0.75);
    }

    private static void AddHorizontalDimension(
        PrimitiveScene scene,
        double y,
        string prefix,
        string textSourceId)
    {
        scene.Lines.Add(new LinePrimitive(new Point2(0, y), new Point2(100, y), SourceIds: [$"{prefix}-dim"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, y - 12), new Point2(0, y + 1), SourceIds: [$"{prefix}-ext-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(100, y - 12), new Point2(100, y + 1), SourceIds: [$"{prefix}-ext-2"]));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, y - 1), new Point2(1, y + 1), SourceIds: [$"{prefix}-arrow-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(99, y - 1), new Point2(101, y + 1), SourceIds: [$"{prefix}-arrow-2"]));
        scene.Texts.Add(new TextPrimitive("5000", new Point2(50, y + 3), 2.5, 0, SourceIds: [textSourceId]));
    }
}
