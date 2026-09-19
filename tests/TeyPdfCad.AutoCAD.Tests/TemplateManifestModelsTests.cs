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
}
