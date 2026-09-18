using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using System.Text.Json;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class VectorTextRecognizerTests
{
    [Fact]
    public void Recognizes_Explicit_SevenSegment_Run_And_Merges_Provenance()
    {
        var scene = new PrimitiveScene();
        AddDigit(scene, '2', 0, ["glyph-2"]);
        AddDigit(scene, '0', 1.2, ["glyph-0"]);
        AddDigit(scene, '3', 2.4, ["glyph-3"]);

        var result = new VectorTextRecognizer().Analyze(scene);

        var text = Assert.Single(result.Texts);
        Assert.Equal("203", text.Value);
        Assert.Equal(new Point2(1.7, 0.5), text.Position);
        Assert.Equal(1, text.Height, 6);
        Assert.Equal(0, text.Rotation, 6);
        Assert.Equal(new[] { "glyph-2", "glyph-0", "glyph-3" }, text.ProvenanceIds);
        Assert.Empty(result.Rejections);
    }

    [Fact]
    public void Rejects_Unknown_Glyph_Without_Fabricating_Text()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(1, 1),
            "PDF _0",
            ["unknown-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(1, 1),
            new Point2(0, 1),
            "PDF _0",
            ["unknown-2"]));

        var result = new VectorTextRecognizer().Analyze(scene);

        Assert.Empty(result.Texts);
        var rejection = Assert.Single(result.Rejections);
        Assert.Equal("no-template-match", rejection.Reason);
        Assert.Equal(new[] { "unknown-1", "unknown-2" }, rejection.ProvenanceIds);
    }

    [Fact]
    public void Does_Not_Mutate_Source_Scene()
    {
        var scene = new PrimitiveScene();
        AddDigit(scene, '8', 0, ["source"]);
        var lineCount = scene.Lines.Count;
        var firstLine = scene.Lines[0];

        _ = new VectorTextRecognizer().Recognize(scene);

        Assert.Equal(lineCount, scene.Lines.Count);
        Assert.Same(firstLine, scene.Lines[0]);
        Assert.Empty(scene.Texts);
    }

    [Fact]
    public void Empty_Template_Set_Fails_Closed()
    {
        var scene = new PrimitiveScene();
        AddDigit(scene, '8', 0, ["source"]);

        var result = new VectorTextRecognizer().Analyze(
            scene,
            new VectorTextRecognitionOptions { Templates = [] });

        Assert.Empty(result.Texts);
        Assert.Empty(result.Rejections);
    }

    [Fact]
    public void Custom_Template_Allows_Font_Specific_Character_Without_Global_Threshold_Changes()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(0, 1), SourceIds: ["a-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 1), new Point2(1, 1), SourceIds: ["a-2"]));

        var template = new VectorGlyphTemplate(
            "A",
            [
                new VectorGlyphTemplateStroke(new Point2(0, 0), new Point2(0, 1)),
                new VectorGlyphTemplateStroke(new Point2(0, 1), new Point2(1, 1)),
            ]);

        var result = new VectorTextRecognizer().Analyze(
            scene,
            new VectorTextRecognitionOptions { Templates = [template] });

        var text = Assert.Single(result.Texts);
        Assert.Equal("A", text.Value);
        Assert.Equal(new[] { "a-1", "a-2" }, text.ProvenanceIds);
    }

    [Fact]
    public void Preserved_Real_VectorGlyph_Fixture_Remains_FailClosed_With_Default_Templates()
    {
        var fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../fixtures/real/autocad2022_vector_glyph_203_212_168.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("texts").GetArrayLength());

        var scene = new PrimitiveScene();
        foreach (var line in root.GetProperty("lines").EnumerateArray())
        {
            var start = line.GetProperty("start");
            var end = line.GetProperty("end");
            var sourceIds = line.GetProperty("sourceIds")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)
                .ToArray();
            scene.Lines.Add(new LinePrimitive(
                new Point2(start[0].GetDouble(), start[1].GetDouble()),
                new Point2(end[0].GetDouble(), end[1].GetDouble()),
                line.GetProperty("layer").GetString(),
                sourceIds));
        }

        var result = new VectorTextRecognizer().Analyze(scene);

        Assert.Empty(result.Texts);
    }

    private static void AddDigit(
        PrimitiveScene scene,
        char value,
        double xOffset,
        IReadOnlyList<string> sourceIds)
    {
        var template = Assert.Single(VectorGlyphTemplates.SevenSegmentDigits, x => x.Value == value.ToString());
        foreach (var stroke in template.Strokes)
        {
            scene.Lines.Add(new LinePrimitive(
                new Point2(xOffset + stroke.Start.X, stroke.Start.Y),
                new Point2(xOffset + stroke.End.X, stroke.End.Y),
                "PDF _0",
                sourceIds));
        }
    }
}
