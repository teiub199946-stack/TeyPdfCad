using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class LevelRecognizerTests
{
    [Fact]
    public void Recognizes_explicit_level_text_with_marker_geometry()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new(10, 10), new(16, 10), SourceIds: ["shelf"]));
        scene.Lines.Add(new LinePrimitive(new(10, 10), new(12, 12), SourceIds: ["marker-a"]));
        scene.Lines.Add(new LinePrimitive(new(10, 10), new(12, 8), SourceIds: ["marker-b"]));
        scene.Texts.Add(new TextPrimitive("±0.000", new(17, 10), 2.5, 0, SourceIds: ["value"]));

        var result = new LevelRecognizer().Recognize(scene);

        var level = Assert.Single(result.NativeLevels);
        Assert.Equal("±0.000", level.Value);
        Assert.Contains("value", level.ProvenanceIds);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Keeps_explicit_level_text_without_marker_as_low_confidence()
    {
        var scene = new PrimitiveScene();
        scene.Texts.Add(new TextPrimitive("+0.000", new(17, 10), 2.5, 0, SourceIds: ["value"]));

        var result = new LevelRecognizer().Recognize(scene);

        Assert.Empty(result.NativeLevels);
        Assert.Contains(result.Warnings, warning => warning.Code == "level-low-confidence");
    }
}
