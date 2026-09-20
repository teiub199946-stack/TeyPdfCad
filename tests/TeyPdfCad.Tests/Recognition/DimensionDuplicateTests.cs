using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class DimensionDuplicateTests
{
    [Fact]
    public void A_single_source_dimension_text_produces_at_most_one_candidate()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new(new(0, 0), new(52, 0), SourceIds: ["D1"]));
        scene.Lines.Add(new(new(0, -12), new(0, 1), SourceIds: ["E1"]));
        scene.Lines.Add(new(new(52, -12), new(52, 1), SourceIds: ["E2"]));
        scene.Lines.Add(new(new(-1, -1), new(1, 1), SourceIds: ["A1"]));
        scene.Lines.Add(new(new(51, -1), new(53, 1), SourceIds: ["A2"]));
        scene.Texts.Add(new("5200", new(26, 3), 2.5, 0, SourceIds: ["T1"]));

        var result = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Single(result, candidate => candidate.ProvenanceIds.Contains("T1"));
    }
}
