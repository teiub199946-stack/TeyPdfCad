using TeyPdfCad.Core;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Tests.Recognition;

public sealed class LinearDimensionRecognizerTests
{
    [Fact]
    public void Recognizes_5200_Horizontal_Dimension_At_1_100()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, -1), new Point2(1, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(51, -1), new Point2(53, 1)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));

        var result = new LinearDimensionRecognizer().Recognize(scene);

        var dimension = Assert.Single(result);
        Assert.Equal(DimensionKind.Rotated, dimension.Kind);
        Assert.Equal(5200, dimension.DisplayedValue, 6);
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.Equal(1, dimension.ArrowEvidence, 6);
        Assert.True(dimension.Confidence >= 0.85);
    }

    [Fact]
    public void Recognizes_Aligned_Dimension_At_45_Degrees()
    {
        const double component = 36.76955262170047;
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(component, component)));
        scene.Lines.Add(new LinePrimitive(new Point2(8.485281374, -8.485281374), new Point2(-0.707106781, 0.707106781)));
        scene.Lines.Add(new LinePrimitive(new Point2(component + 8.485281374, component - 8.485281374), new Point2(component - 0.707106781, component + 0.707106781)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(16.263455967, 20.506096654), 2.5, 45));

        var result = new LinearDimensionRecognizer().Recognize(scene);

        var dimension = Assert.Single(result);
        Assert.Equal(DimensionKind.Aligned, dimension.Kind);
        Assert.Equal(100, dimension.DrawingScale, 5);
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 3);
    }

    [Fact]
    public void Rejects_Number_Next_To_Ordinary_Line_Without_Extension_Lines()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));

        var result = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Empty(result);
    }
}
