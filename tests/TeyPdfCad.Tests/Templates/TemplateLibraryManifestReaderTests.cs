using TeyPdfCad.Core.Templates;
using Xunit;

namespace TeyPdfCad.Tests.Templates;

public sealed class TemplateLibraryManifestReaderTests
{
    [Fact]
    public void Reads_reconstructable_linework_and_attributes_from_autocad_manifest()
    {
        const string json = """
        { "schemaVersion":"1", "blocks":[{
          "name":"A3-landscape",
          "entities":[{"objectClass":"AcDbLine","handle":"A1","minX":0,"minY":0,"maxX":420,"maxY":0,"points":[{"x":0,"y":0},{"x":420,"y":0}],"layer":"0"}],
          "attributes":[{"tag":"SHEET","prompt":"Лист","defaultValue":"1"}]
        }], "styles":[], "unsupportedEntityClasses":[] }
        """;

        var library = new TemplateLibraryManifestReader().Read(json);

        var block = Assert.Single(library.Blocks);
        Assert.Equal("A3-landscape", block.Name);
        Assert.Equal(2, Assert.Single(block.Entities).Points.Count);
        Assert.Equal("SHEET", Assert.Single(block.Attributes).Tag);
    }
}
