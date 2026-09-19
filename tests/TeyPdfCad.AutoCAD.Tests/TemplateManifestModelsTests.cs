using TeyPdfCad.AutoCAD;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class TemplateManifestModelsTests
{
    [Fact]
    public void Manifest_serializes_block_attributes_and_unknown_classes()
    {
        var manifest = new TemplateLibraryManifest(
            "1",
            [new TemplateBlockManifest("A3", [], [new TemplateAttributeManifest("SHEET", "Лист", "1")])],
            [new TemplateStyleManifest("ГОСТ 2.304", "TextStyle")],
            ["AeccDbProxyEntity"]);

        var json = manifest.ToJson();

        Assert.Contains("AeccDbProxyEntity", json);
        Assert.Contains("\"A3\"", json);
        Assert.Contains("\"SHEET\"", json);
    }

    [Fact]
    public void Manifest_serializes_reconstructable_line_geometry_and_text()
    {
        var entity = new TemplateEntityManifest(
            "AcDbLine", "A1", 0, 0, 10, 0,
            [new TemplatePointManifest(0, 0), new TemplatePointManifest(10, 0)],
            "Рамка", 3.5, "0");
        var manifest = new TemplateLibraryManifest("1", [new TemplateBlockManifest("A3", [entity], [])], [], []);

        var json = manifest.ToJson();

        Assert.Contains("\"points\"", json);
        Assert.Contains("\"Рамка\"", json);
        Assert.Contains("\"textHeight\": 3.5", json);
    }
}
