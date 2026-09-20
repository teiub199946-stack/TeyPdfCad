using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class AxisRecognizerTests
{
    [Fact]
    public void Reconstructs_collinear_dash_dot_segments_as_one_axis()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new(0, 10), new(30, 10), SourceIds: ["dash-1"]));
        scene.Lines.Add(new LinePrimitive(new(34, 10), new(38, 10), SourceIds: ["dot"]));
        scene.Lines.Add(new LinePrimitive(new(42, 10), new(72, 10), SourceIds: ["dash-2"]));

        var result = new AxisRecognizer().Recognize(scene);

        var axis = Assert.Single(result.NativeAxes);
        Assert.Equal(new Point2(0, 10), axis.Start);
        Assert.Equal(new Point2(72, 10), axis.End);
        Assert.Equal(3, axis.ProvenanceIds.Count);
    }

    [Fact]
    public void Dash_dot_centerline_is_recognized_as_an_axis()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 20),
            new Point2(120, 20),
            Layer: "ОСИ",
            SourceIds: ["axis-1"],
            DashPatternMm: [12, 3, 2, 3]));

        var result = new AxisRecognizer().Recognize(scene);

        var axis = Assert.Single(result.NativeAxes);
        Assert.Equal("axis-1", Assert.Single(axis.ProvenanceIds));
        Assert.True(axis.Confidence >= 0.9);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Solid_line_on_an_unrelated_layer_stays_geometry()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 20), new Point2(120, 20), SourceIds: ["line-1"]));

        var result = new AxisRecognizer().Recognize(scene);

        Assert.Empty(result.NativeAxes);
        Assert.Contains(result.Warnings, warning => warning.Code == "axis-low-confidence");
    }
}
