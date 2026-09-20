using TeyPdfCad.Core.Templates;
using Xunit;

namespace TeyPdfCad.Tests.Templates;

public sealed class TemplateLibraryManifestReaderTests
{
    [Fact]
    public void Infers_sheet_origin_from_geometry_when_legacy_manifest_origin_is_zero()
    {
        const string json = """
        { "schemaVersion":"1", "blocks":[{
          "name":"A3-landscape",
          "origin":{"x":0,"y":0},
          "entities":[
            {"objectClass":"AcDbLine","handle":"A1","points":[{"x":570401.7,"y":35136.4},{"x":570821.7,"y":35136.4}]},
            {"objectClass":"AcDbLine","handle":"A2","points":[{"x":570401.7,"y":35136.4},{"x":570401.7,"y":35433.4}]}
          ],
          "attributes":[]
        }] }
        """;

        var block = Assert.Single(new TemplateLibraryManifestReader().Read(json).Blocks);

        Assert.Equal(570401.7, block.EffectiveOrigin.X, 6);
        Assert.Equal(35136.4, block.EffectiveOrigin.Y, 6);
    }

    [Fact]
    public void Does_not_register_named_sheet_when_exported_block_has_no_reconstructable_geometry()
    {
        const string json = """
        { "schemaVersion":"1", "blocks":[{
          "name":"A3-landscape",
          "origin":{"x":570401.723,"y":35136.433},
          "entities":[],
          "attributes":[]
        }], "styles":[], "unsupportedEntityClasses":["mcsDbObjectFormat"] }
        """;

        var library = new TemplateLibraryManifestReader().Read(json);

        Assert.Empty(library.Sheets);
        Assert.Empty(Assert.Single(library.Blocks).Entities);
    }

    [Fact]
    public void Reads_reconstructable_linework_and_attributes_from_autocad_manifest()
    {
        const string json = """
        { "schemaVersion":"1", "blocks":[{
          "name":"A3-landscape",
          "origin":{"x":1000,"y":2000},
          "entities":[
            {"objectClass":"AcDbLine","handle":"A1","minX":1000,"minY":2000,"maxX":1420,"maxY":2000,"points":[{"x":1000,"y":2000},{"x":1420,"y":2000}],"layer":"0"},
            {"objectClass":"AcDbPolyline","handle":"A2","minX":1000,"minY":2000,"maxX":1010,"maxY":2010,"points":[{"x":1000,"y":2000},{"x":1010,"y":2000},{"x":1010,"y":2010}],"layer":"0","isClosed":true},
            {"objectClass":"AcDbText","handle":"A3","minX":1005,"minY":2005,"maxX":1020,"maxY":2010,"points":[{"x":1005,"y":2005}],"text":"Лист","textHeight":3.5,"rotationRadians":1.5707963267948966,"layer":"0"},
            {"objectClass":"AcDbArc","handle":"A4","minX":1000,"minY":2000,"maxX":1010,"maxY":2010,"points":[{"x":1005,"y":2005}],"arcRadius":5,"startAngleRadians":0,"endAngleRadians":1.5707963267948966,"layer":"0"}
          ],
          "attributes":[{"tag":"SHEET","prompt":"Лист","defaultValue":"1"}]
        }], "styles":[], "unsupportedEntityClasses":[] }
        """;

        var library = new TemplateLibraryManifestReader().Read(json);

        var block = Assert.Single(library.Blocks);
        Assert.Equal("A3-landscape", block.Name);
        Assert.Equal(2, block.Entities[0].Points.Count);
        Assert.Equal(new TemplatePoint(1000, 2000), block.Origin);
        Assert.True(block.Entities[1].IsClosed);
        Assert.Equal(Math.PI / 2d, block.Entities[2].RotationRadians, 12);
        Assert.Equal(5d, block.Entities[3].ArcRadius!.Value, 12);
        Assert.Equal(Math.PI / 2d, block.Entities[3].EndAngleRadians!.Value, 12);
        Assert.Equal("SHEET", Assert.Single(block.Attributes).Tag);
        var sheet = Assert.Single(library.Sheets);
        Assert.Equal(TeyPdfCad.Core.Sheets.StandardSheetFormat.A3, sheet.Format);
        Assert.Equal(TeyPdfCad.Core.Sheets.SheetOrientation.Landscape, sheet.Orientation);
    }
}
