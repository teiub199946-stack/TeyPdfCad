using TeyPdfCad.AutoCAD;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class TemplateManifestModelsTests
{
    [Fact]
    public void Expansion_flattens_nested_custom_entities_to_supported_leaf_geometry()
    {
        var root = new ExpansionNode("mcsDbObjectFormat",
        [
            new ExpansionNode("AcDbBlockReference",
            [
                new ExpansionNode("AcDbLine"),
                new ExpansionNode("AcDbMText")
            ])
        ]);

        var leaves = TemplateEntityExpansion.Flatten(
            root,
            node => node.Children.Count == 0 && node.Name is "AcDbLine" or "AcDbMText",
            node => node.Children);

        Assert.Equal(["AcDbLine", "AcDbMText"], leaves.Select(node => node.Name));
    }

    [Fact]
    public void Expansion_keeps_unexpandable_entity_for_unsupported_diagnostics()
    {
        var root = new ExpansionNode("mcsDbObjectFormat");

        var leaves = TemplateEntityExpansion.Flatten(
            root,
            node => node.Name == "AcDbLine",
            node => node.Children);

        Assert.Same(root, Assert.Single(leaves));
    }

    [Fact]
    public void Expansion_releases_every_generated_node_after_its_leaf_is_consumed()
    {
        var line = new ExpansionNode("AcDbLine");
        var nested = new ExpansionNode("AcDbBlockReference", [line]);
        var root = new ExpansionNode("mcsDbObjectFormat", [nested]);
        var visited = new List<string>();
        var released = new List<string>();

        TemplateEntityExpansion.VisitLeaves(
            root,
            node => node.Name == "AcDbLine",
            node => node.Children,
            node => visited.Add(node.Name),
            node => released.Add(node.Name));

        Assert.Equal(["AcDbLine"], visited);
        Assert.Equal(["AcDbLine", "AcDbBlockReference"], released);
    }

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

    private sealed record ExpansionNode(string Name, IReadOnlyList<ExpansionNode>? Items = null)
    {
        public IReadOnlyList<ExpansionNode> Children => Items ?? [];
    }
}
