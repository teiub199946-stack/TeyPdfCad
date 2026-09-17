using Xunit;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Tests.Recognition;

public sealed class DimensionProvenanceTests
{
    [Fact]
    public void Carries_Source_Ids_Into_Reconstructed_Dimension()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0), SourceIds: ["D1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1), SourceIds: ["E1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1), SourceIds: ["E2"]));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0, SourceIds: ["T1"]));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Equal(4, dimension.ProvenanceIds.Count);
        Assert.Contains("D1", dimension.ProvenanceIds);
        Assert.Contains("E1", dimension.ProvenanceIds);
        Assert.Contains("E2", dimension.ProvenanceIds);
        Assert.Contains("T1", dimension.ProvenanceIds);
    }

    [Fact]
    public void Carries_Both_Split_Line_Source_Ids()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(22, 0), SourceIds: ["D1A"]));
        scene.Lines.Add(new LinePrimitive(new Point2(30, 0), new Point2(52, 0), SourceIds: ["D1B"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1), SourceIds: ["E1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1), SourceIds: ["E2"]));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0, SourceIds: ["T1"]));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Contains("D1A", dimension.ProvenanceIds);
        Assert.Contains("D1B", dimension.ProvenanceIds);
        Assert.Contains("T1", dimension.ProvenanceIds);
    }
}
