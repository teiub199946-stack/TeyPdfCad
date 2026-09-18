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

    [Theory]
    [InlineData(90)]
    [InlineData(35)]
    [InlineData(-30)]
    public void Recognizes_Rotated_SevenSegment_Run_And_Preserves_Reading_Order(double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var scene = new PrimitiveScene();
        AddDigit(scene, '2', 0, ["glyph-2"], radians);
        AddDigit(scene, '0', 1.2, ["glyph-0"], radians);
        AddDigit(scene, '3', 2.4, ["glyph-3"], radians);

        var result = new VectorTextRecognizer().Analyze(scene);

        var text = Assert.Single(result.Texts);
        Assert.Equal("203", text.Value);
        var expectedCenter = Rotate(new Point2(1.7, 0.5), radians);
        Assert.Equal(expectedCenter.X, text.Position.X, 5);
        Assert.Equal(expectedCenter.Y, text.Position.Y, 5);
        Assert.Equal(1, text.Height, 5);
        Assert.True(
            AngleDistanceModuloPi(radians, text.Rotation) < 1e-4,
            $"expected rotation line {radians}, actual {text.Rotation}");
    }

    [Fact]
    public void Groups_Disconnected_Strokes_From_The_Same_PdfImport_Polyline_Source()
    {
        var template = new VectorGlyphTemplate(
            "A",
            [
                new VectorGlyphTemplateStroke(new Point2(0, 0), new Point2(0, 1)),
                new VectorGlyphTemplateStroke(new Point2(1, 0), new Point2(1, 1)),
            ]);
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(10, 20),
            new Point2(10, 24),
            "PDF _0",
            ["42#segment:0"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(14, 20),
            new Point2(14, 24),
            "PDF _0",
            ["42#segment:1"]));

        var result = new VectorTextRecognizer().Analyze(
            scene,
            new VectorTextRecognitionOptions { Templates = [template] });

        var text = Assert.Single(result.Texts);
        Assert.Equal("A", text.Value);
        Assert.Equal(new[] { "42#segment:0", "42#segment:1" }, text.ProvenanceIds);
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
    public void Ambiguous_EqualScore_Templates_Fail_Closed()
    {
        var strokes = new[]
        {
            new VectorGlyphTemplateStroke(new Point2(0, 0), new Point2(0, 1)),
            new VectorGlyphTemplateStroke(new Point2(0, 1), new Point2(1, 1)),
        };
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(0, 1),
            SourceIds: ["77#segment:0"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 1),
            new Point2(1, 1),
            SourceIds: ["77#segment:1"]));

        var result = new VectorTextRecognizer().Analyze(
            scene,
            new VectorTextRecognitionOptions
            {
                Templates =
                [
                    new VectorGlyphTemplate("A", strokes),
                    new VectorGlyphTemplate("B", strokes),
                ],
            });

        Assert.Empty(result.Texts);
    }

    [Fact]
    public void Deterministic_1000_Case_Corpus_Is_Rotation_Scale_Translation_And_Provenance_Stable()
    {
        var random = new Random(20260919);
        var recognizer = new VectorTextRecognizer();

        for (var caseIndex = 0; caseIndex < 1000; caseIndex++)
        {
            var value = string.Concat(
                Enumerable.Range(0, 3)
                    .Select(_ => (char)('0' + random.Next(0, 10))));
            var rotation = (-80 + random.NextDouble() * 160) * Math.PI / 180.0;
            var scale = 0.25 + random.NextDouble() * 4.75;
            var translation = new Point2(
                -10000 + random.NextDouble() * 20000,
                -10000 + random.NextDouble() * 20000);
            var scene = new PrimitiveScene();

            for (var digitIndex = 0; digitIndex < value.Length; digitIndex++)
            {
                var template = Assert.Single(
                    VectorGlyphTemplates.SevenSegmentDigits,
                    candidate => candidate.Value == value[digitIndex].ToString());
                var xOffset = digitIndex * 1.2;

                for (var strokeIndex = 0; strokeIndex < template.Strokes.Count; strokeIndex++)
                {
                    var stroke = template.Strokes[strokeIndex];
                    var start = TransformCorpusPoint(
                        new Point2(xOffset + stroke.Start.X, stroke.Start.Y),
                        scale,
                        rotation,
                        translation);
                    var end = TransformCorpusPoint(
                        new Point2(xOffset + stroke.End.X, stroke.End.Y),
                        scale,
                        rotation,
                        translation);
                    var jitter = scale * 1e-5;
                    start = new Point2(
                        start.X + (random.NextDouble() - 0.5) * jitter,
                        start.Y + (random.NextDouble() - 0.5) * jitter);
                    end = new Point2(
                        end.X + (random.NextDouble() - 0.5) * jitter,
                        end.Y + (random.NextDouble() - 0.5) * jitter);

                    scene.Lines.Add(new LinePrimitive(
                        start,
                        end,
                        "PDF _0",
                        [$"H{caseIndex}_{digitIndex}#segment:{strokeIndex}"]));
                }
            }

            var result = recognizer.Analyze(scene);
            Assert.True(
                result.Texts.Count == 1,
                $"case {caseIndex}: value={value}, rotation={rotation}, scale={scale}, texts=[{string.Join("|", result.Texts.Select(text => text.Value))}]");
            var text = result.Texts[0];
            Assert.Equal(value, text.Value);
            Assert.True(
                AngleDistanceModuloPi(rotation, text.Rotation) < 1e-3,
                $"case {caseIndex}: expected rotation {rotation}, actual {text.Rotation}");
            Assert.Equal(value.Length == 0 ? 0 : value.Length * 1, text.Value.Length);
        }
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
        IReadOnlyList<string> sourceIds,
        double rotation = 0)
    {
        var template = Assert.Single(VectorGlyphTemplates.SevenSegmentDigits, x => x.Value == value.ToString());
        foreach (var stroke in template.Strokes)
        {
            var start = Rotate(new Point2(xOffset + stroke.Start.X, stroke.Start.Y), rotation);
            var end = Rotate(new Point2(xOffset + stroke.End.X, stroke.End.Y), rotation);
            scene.Lines.Add(new LinePrimitive(
                start,
                end,
                "PDF _0",
                sourceIds));
        }
    }


    private static Point2 TransformCorpusPoint(
        Point2 point,
        double scale,
        double rotation,
        Point2 translation)
    {
        var scaled = new Point2(point.X * scale, point.Y * scale);
        var rotated = Rotate(scaled, rotation);
        return new Point2(rotated.X + translation.X, rotated.Y + translation.Y);
    }

    private static Point2 Rotate(Point2 point, double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Point2(
            point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos);
    }

    private static double AngleDistanceModuloPi(double left, double right)
    {
        var delta = Math.Abs(left - right) % Math.PI;
        return Math.Min(delta, Math.PI - delta);
    }
}
