using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class ArcDimensionRecognizerTests
{
    [Fact]
    public void Recognizes_explicit_arc_length_label_near_arc()
    {
        var scene = new PrimitiveScene();
        scene.Arcs.Add(new ArcPrimitive(new(50, 50), 20, 0, Math.PI / 2, SourceIds: ["arc"]));
        scene.Texts.Add(new TextPrimitive("L=31,42", new(65, 65), 2.5, 0, SourceIds: ["label"]));

        var result = new ArcDimensionRecognizer().Recognize(scene);

        var dimension = Assert.Single(result.NativeArcDimensions);
        Assert.Equal(20, dimension.Radius, 6);
        Assert.Equal("L=31,42", dimension.SourceText);
        Assert.Contains("arc", dimension.ProvenanceIds);
        Assert.Contains("label", dimension.ProvenanceIds);
    }

    [Fact]
    public void Does_not_treat_radius_label_as_arc_length()
    {
        var scene = new PrimitiveScene();
        scene.Arcs.Add(new ArcPrimitive(new(50, 50), 20, 0, Math.PI / 2, SourceIds: ["arc"]));
        scene.Texts.Add(new TextPrimitive("R20", new(65, 65), 2.5, 0, SourceIds: ["label"]));

        var result = new ArcDimensionRecognizer().Recognize(scene);

        Assert.Empty(result.NativeArcDimensions);
    }
}
