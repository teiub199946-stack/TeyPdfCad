using Xunit;
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

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(DimensionKind.Rotated, dimension.Kind);
        Assert.Equal(5200, dimension.DisplayedValue, 6);
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.Equal(1, dimension.ArrowEvidence, 6);
        Assert.True(dimension.Confidence >= 0.85);
    }

    [Fact]
    public void Recognizes_Dimension_Line_Split_Around_Text()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(22, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(30, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
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

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(DimensionKind.Aligned, dimension.Kind);
        Assert.Equal(100, dimension.DrawingScale, 5);
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 3);
    }

    [Fact]
    public void Accepts_Small_PdfImport_Coordinate_Noise()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0.01, -0.01), new Point2(52.02, 0.02)));
        scene.Lines.Add(new LinePrimitive(new Point2(-0.02, -12), new Point2(0.04, 1.01)));
        scene.Lines.Add(new LinePrimitive(new Point2(52.03, -12.02), new Point2(51.98, 1.02)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26.01, 3.02), 2.5, 0));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.InRange(dimension.ReconstructedMeasurement, 5190, 5210);
    }

    [Fact]
    public void Recognizes_NonCanonical_Scale_From_Two_Dimension_Consensus()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 50, displayedValue: "4800");
        AddHorizontalDimension(scene, y: 30, importedLength: 75, displayedValue: "7200");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, d => Assert.Equal(96, d.DrawingScale, 6));
        Assert.Contains(dimensions, d => Math.Abs(d.DisplayedValue - 4800) < 1e-6);
        Assert.Contains(dimensions, d => Math.Abs(d.DisplayedValue - 7200) < 1e-6);
    }

    [Fact]
    public void Scale_Consensus_Rejects_Single_Outlier_With_Different_Scale()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 50, displayedValue: "4800");
        AddHorizontalDimension(scene, y: 30, importedLength: 75, displayedValue: "7200");
        AddHorizontalDimension(scene, y: 60, importedLength: 10, displayedValue: "5000");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, d => Assert.Equal(96, d.DrawingScale, 6));
        Assert.DoesNotContain(dimensions, d => Math.Abs(d.DisplayedValue - 5000) < 1e-6);
    }

    [Fact]
    public void Rejects_Number_Next_To_Ordinary_Line_Without_Extension_Lines()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));
        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    [Fact]
    public void Rejects_Distant_Lines_That_Only_Intersect_Endpoints_When_Extended()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 100), new Point2(0, 120)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, 100), new Point2(52, 120)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));
        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    private static void AddHorizontalDimension(
        PrimitiveScene scene,
        double y,
        double importedLength,
        string displayedValue)
    {
        scene.Lines.Add(new LinePrimitive(new Point2(0, y), new Point2(importedLength, y)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, y - 12), new Point2(0, y + 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(importedLength, y - 12), new Point2(importedLength, y + 1)));
        scene.Texts.Add(new TextPrimitive(displayedValue, new Point2(importedLength / 2.0, y + 3), 2.5, 0));
    }
}
