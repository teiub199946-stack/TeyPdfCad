using Xunit;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Tests.Recognition;

public sealed class SemanticReconstructionEngineTests
{
    [Fact]
    public void Reconstructs_Three_Dimension_Chain_And_Dominant_Scale()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(12, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(12, 0), new Point2(30, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(30, 0), new Point2(52, 0)));
        foreach (var x in new[] { 0.0, 12.0, 30.0, 52.0 })
            scene.Lines.Add(new LinePrimitive(new Point2(x, -12), new Point2(x, 1)));

        scene.Texts.Add(new TextPrimitive("1200", new Point2(6, 3), 2.5, 0));
        scene.Texts.Add(new TextPrimitive("1800", new Point2(21, 3), 2.5, 0));
        scene.Texts.Add(new TextPrimitive("2200", new Point2(41, 3), 2.5, 0));

        var result = new SemanticReconstructionEngine().Analyze(scene);

        Assert.Equal(3, result.Dimensions.Count);
        var chain = Assert.Single(result.DimensionChains);
        Assert.Equal(5200, chain.TotalDisplayedValue, 6);
        Assert.Equal(100, result.DominantDrawingScale);
        Assert.True(result.AverageDimensionConfidence >= 0.75);
    }
}
