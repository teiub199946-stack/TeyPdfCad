using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Dwg;
using TeyPdfCad.Core.Geometry;
using ACadSharp.IO;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DwgDocumentWriterTests
{
    [Fact]
    public void Writer_type_is_available_without_autocad()
    {
        var document = new VectorPdfDocument([new VectorPdfPage(1, 595.276, 841.89, 0, [])]);
        var plan = new DocumentLayoutPlanner().Create(document);

        Assert.NotNull(new AcadSharpDwgWriter());
        Assert.Single(plan.Sheets);
    }

    [Fact]
    public void Writer_creates_a_binary_dwg_without_autocad()
    {
        var document = new VectorPdfDocument([new VectorPdfPage(1, 595.276, 841.89, 0, [])]);
        var plan = new DocumentLayoutPlanner().Create(document);

        var bytes = new AcadSharpDwgWriter().Write(document, plan);

        Assert.True(bytes.Length > 32);
        Assert.Equal("AC", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
    }

    [Fact]
    public void Writer_emits_source_lines_into_model_space()
    {
        var page = new VectorPdfPage(1, 595.276, 841.89, 0,
            [new VectorLine("line-1", new Point2(10, 20), new Point2(30, 40), new VectorStyle())]);
        var plan = new DocumentLayoutPlanner().Create(new VectorPdfDocument([page]));

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(new VectorPdfDocument([page]), plan)));

        var line = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Equal(10, line.StartPoint.X);
        Assert.Equal(40, line.EndPoint.Y);
    }
}
