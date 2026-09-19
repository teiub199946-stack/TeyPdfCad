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

    [Fact]
    public void Writer_emits_editable_polylines()
    {
        var page = new VectorPdfPage(1, 595.276, 841.89, 0,
            [new VectorPolyline("poly-1", [new(0, 0), new(10, 0), new(10, 5)], true, new VectorStyle())]);
        var plan = new DocumentLayoutPlanner().Create(new VectorPdfDocument([page]));

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(new VectorPdfDocument([page]), plan)));

        var polyline = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>());
        Assert.True(polyline.IsClosed);
        Assert.Equal(3, polyline.Vertices.Count);
    }

    [Fact]
    public void Writer_creates_one_named_layout_per_source_page()
    {
        var document = new VectorPdfDocument(Enumerable.Range(1, 3)
            .Select(number => new VectorPdfPage(number, 595.276, 841.89, 0, []))
            .ToArray());
        var plan = new DocumentLayoutPlanner().Create(document);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, plan)));

        Assert.Contains(drawing.Layouts, layout => layout.Name == "Лист-001");
        Assert.Contains(drawing.Layouts, layout => layout.Name == "Лист-003");
        var firstLayout = drawing.Layouts.Single(layout => layout.Name == "Лист-001");
        Assert.Equal(210, firstLayout.PaperWidth, 3);
        Assert.Equal(297, firstLayout.PaperHeight, 3);
        var frame = Assert.Single(firstLayout.AssociatedBlock.Entities.OfType<ACadSharp.Entities.LwPolyline>());
        Assert.True(frame.IsClosed);
        Assert.Equal(4, frame.Vertices.Count);
    }
}
