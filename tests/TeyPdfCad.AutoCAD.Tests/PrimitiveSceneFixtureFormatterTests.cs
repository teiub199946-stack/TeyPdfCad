using System.Globalization;
using System.Reflection;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Sheets;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class PrimitiveSceneFixtureFormatterTests
{
    [Fact]
    public void SerializesOptionalSheetMetadata()
    {
        var scene = new PrimitiveScene
        {
            Sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape),
        };

        var json = InvokeFormat(scene, 0, 4);

        Assert.Contains("\"sheet\":{\"widthMm\":420,\"heightMm\":297,\"format\":\"A3\",\"orientation\":\"Landscape\"}", json);
    }

    [Fact]
    public void Serializes_page_scale_with_page_bounds()
    {
        var scene = new PrimitiveScene
        {
            Sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape)
            {
                PageBounds = new SheetPageBounds(2829, 2065, 420, 297, DrawingUnitsPerMm: 100),
            },
        };

        var json = InvokeFormat(scene, 0, 4);

        Assert.Contains("\"pageBounds\":{\"minX\":2829,\"minY\":2065,\"widthMm\":420,\"heightMm\":297,\"drawingUnitsPerMm\":100,\"units\":\"mm\"}", json);
    }

    [Fact]
    public void SerializesEditableTitleBlockContractWithoutInventingFieldSemantics()
    {
        var scene = new PrimitiveScene
        {
            TitleBlock = new TitleBlockMetadata(
                new TitleBlockRegion(290, 4, 420, 75, ["L1", "L2"]),
                [new TitleBlockField(
                    TitleBlockFieldKind.Unknown,
                    "Наименование",
                    new Point2(300, 30),
                    3,
                    0,
                    ["T1"])])
        };
        scene.TitleBlock = scene.TitleBlock! with
        {
            Lines = [new LinePrimitive(new Point2(290, 4), new Point2(420, 4), "TB", ["L1"])],
        };

        var json = InvokeFormat(scene, 3, 4);

        Assert.Contains("\"titleBlock\":{\"isCandidate\":true", json);
        Assert.Contains("\"region\":{\"minX\":290,\"minY\":4,\"maxX\":420,\"maxY\":75,\"sourceIds\":[\"L1\",\"L2\"]}", json);
        Assert.Contains("\"kind\":\"Unknown\",\"value\":\"Наименование\"", json);
        Assert.Contains("\"sourceIds\":[\"T1\"]", json);
        Assert.Contains("\"lines\":[{\"start\":[290,4],\"end\":[420,4]", json);
    }

    [Fact]
    public void Format_is_byte_stable_when_scene_insertion_order_changes()
    {
        var first = new PrimitiveScene();
        first.Lines.Add(new LinePrimitive(new Point2(10, 2), new Point2(11, 3), "A", new[] { "L2" }));
        first.Lines.Add(new LinePrimitive(new Point2(1, 2), new Point2(3, 4), "0", new[] { "L1" }));
        first.Texts.Add(new TextPrimitive("5200", new Point2(4, 5), 2.5, 0.25, "TXT", new[] { "T1" }));

        var second = new PrimitiveScene();
        second.Texts.Add(new TextPrimitive("5200", new Point2(4, 5), 2.5, 0.25, "TXT", new[] { "T1" }));
        second.Lines.Add(new LinePrimitive(new Point2(1, 2), new Point2(3, 4), "0", new[] { "L1" }));
        second.Lines.Add(new LinePrimitive(new Point2(10, 2), new Point2(11, 3), "A", new[] { "L2" }));

        var firstJson = InvokeFormat(first, 3, 4);
        var secondJson = InvokeFormat(second, 3, 4);

        Assert.Equal(firstJson, secondJson);
    }

    [Fact]
    public void Format_preserves_geometry_layer_provenance_and_units()
    {
        const double expectedRotation = 0.7853981633974483;
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(1.25, -2.5),
            new Point2(3.75, 4.5),
            "DIM",
            new[] { "AB#segment:2", "AB" }));
        scene.Texts.Add(new TextPrimitive(
            "5200",
            new Point2(8.5, 9.25),
            2.5,
            expectedRotation,
            "TEXT",
            new[] { "CD" }));

        var json = InvokeFormat(scene, 35, 4);

        Assert.Contains("\"schema\":\"TeyPdfCad.PrimitiveScene.v1\"", json);
        Assert.Contains("\"selectedCount\":35", json);
        Assert.Contains("\"insunits\":4", json);
        Assert.Contains("\"start\":[1.25,-2.5]", json);
        Assert.Contains("\"end\":[3.75,4.5]", json);
        Assert.Contains("\"layer\":\"DIM\"", json);
        Assert.Contains("\"sourceIds\":[\"AB\",\"AB#segment:2\"]", json);
        Assert.Contains("\"value\":\"5200\"", json);
        Assert.Contains("\"position\":[8.5,9.25]", json);
        Assert.Contains("\"height\":2.5", json);
        Assert.Equal(expectedRotation, ExtractNumber(json, "\"rotation\":"));
    }

    [Fact]
    public void Format_json_escapes_strings_and_handles_empty_text_scene()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(1, 1),
            "A\"B\\C\nD",
            new[] { "S\"1\\2" }));

        var json = InvokeFormat(scene, 1, 0);

        Assert.Contains("A\\\"B\\\\C\\nD", json);
        Assert.Contains("S\\\"1\\\\2", json);
        Assert.Contains("\"texts\":[]", json);
    }

    private static double ExtractNumber(string json, string marker)
    {
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker '{marker}' was not found.");
        start += marker.Length;

        var end = start;
        while (end < json.Length && "-+0123456789.eE".IndexOf(json[end]) >= 0)
            end++;

        var token = json.Substring(start, end - start);
        Assert.True(double.TryParse(
            token,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value),
            $"'{token}' is not a valid invariant floating-point number.");
        return value;
    }

    private static string InvokeFormat(PrimitiveScene scene, int selectedCount, int insunits)
    {
        var assembly = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly;
        var formatterType = assembly.GetType("TeyPdfCad.AutoCAD.PrimitiveSceneFixtureFormatter");
        Assert.NotNull(formatterType);

        var method = formatterType!.GetMethod(
            "Format",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(PrimitiveScene), typeof(int), typeof(int) },
            modifiers: null);

        Assert.NotNull(method);
        var result = (string?)method!.Invoke(null, new object[] { scene, selectedCount, insunits });
        Assert.NotNull(result);
        return result!;
    }
}
