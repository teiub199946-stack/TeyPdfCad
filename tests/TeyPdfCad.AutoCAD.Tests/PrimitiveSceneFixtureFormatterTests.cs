using System.Reflection;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class PrimitiveSceneFixtureFormatterTests
{
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
            0.7853981633974483,
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
        Assert.Contains("\"rotation\":0.7853981633974483", json);
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
