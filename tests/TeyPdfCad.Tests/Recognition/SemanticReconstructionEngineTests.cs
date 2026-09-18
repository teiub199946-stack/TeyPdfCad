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
        var scale = Assert.Single(result.DetectedDrawingScales);
        Assert.Equal(100, scale, 6);
        Assert.True(result.AverageDimensionConfidence >= 0.75);
    }

    [Fact]
    public void OptIn_VectorText_Preprocessing_Feeds_Dimensions_Without_Mutating_Source_Scene()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(12, 0), SourceIds: ["dim"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1), SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(12, -12), new Point2(12, 1), SourceIds: ["ext-2"]));
        AddVectorNumber(scene, "1200", new Point2(3.7, 2.5), 0);

        Assert.Empty(scene.Texts);

        var result = new SemanticReconstructionEngine().Analyze(
            scene,
            vectorTextOptions: new VectorTextRecognitionOptions());

        var dimension = Assert.Single(result.Dimensions);
        Assert.Equal(1200, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.Equal("1200", dimension.SourceText);
        Assert.Empty(scene.Texts);
    }


    [Theory]
    [InlineData(35)]
    [InlineData(90)]
    public void OptIn_VectorText_Preprocessing_Reconstructs_Rotated_Dimensions(double degrees)
    {
        var rotation = degrees * Math.PI / 180.0;
        var scene = new PrimitiveScene();

        scene.Lines.Add(new LinePrimitive(
            Transform(new Point2(0, 0), new Point2(0, 0), rotation),
            Transform(new Point2(12, 0), new Point2(0, 0), rotation),
            SourceIds: ["dim"]));
        scene.Lines.Add(new LinePrimitive(
            Transform(new Point2(0, -12), new Point2(0, 0), rotation),
            Transform(new Point2(0, 1), new Point2(0, 0), rotation),
            SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(
            Transform(new Point2(12, -12), new Point2(0, 0), rotation),
            Transform(new Point2(12, 1), new Point2(0, 0), rotation),
            SourceIds: ["ext-2"]));

        var rotatedOffset = Transform(new Point2(3.7, 2.5), new Point2(0, 0), rotation);
        AddVectorNumber(scene, "1200", rotatedOffset, rotation);

        var result = new SemanticReconstructionEngine().Analyze(
            scene,
            vectorTextOptions: new VectorTextRecognitionOptions());

        var dimension = Assert.Single(result.Dimensions);
        Assert.Equal(1200, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.Equal("1200", dimension.SourceText);
        Assert.Empty(scene.Texts);
    }

    [Fact]
    public void Default_Analyze_Does_Not_Enable_VectorText_Implicitly()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(12, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(12, -12), new Point2(12, 1)));
        AddVectorNumber(scene, "1200", new Point2(3.7, 2.5), 0);

        var result = new SemanticReconstructionEngine().Analyze(scene);

        Assert.Empty(result.Dimensions);
        Assert.Empty(scene.Texts);
    }

    private static void AddVectorNumber(
        PrimitiveScene scene,
        string value,
        Point2 offset,
        double rotation)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var template = Assert.Single(
                VectorGlyphTemplates.SevenSegmentDigits,
                candidate => candidate.Value == value[index].ToString());
            var xOffset = index * 1.2;

            foreach (var stroke in template.Strokes)
            {
                var start = Transform(
                    new Point2(xOffset + stroke.Start.X, stroke.Start.Y),
                    offset,
                    rotation);
                var end = Transform(
                    new Point2(xOffset + stroke.End.X, stroke.End.Y),
                    offset,
                    rotation);
                scene.Lines.Add(new LinePrimitive(
                    start,
                    end,
                    "PDF _0",
                    [$"glyph-{index}"]));
            }
        }
    }

    private static Point2 Transform(Point2 point, Point2 offset, double rotation)
    {
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        return new Point2(
            point.X * cos - point.Y * sin + offset.X,
            point.X * sin + point.Y * cos + offset.Y);
    }
}
