using TeyPdfCad.Core.Documents;
using Xunit;

namespace TeyPdfCad.Tests.Documents;

public sealed class VectorPdfDocumentTests
{
    [Fact]
    public void Page_converts_pdf_points_to_millimetres_exactly()
    {
        var page = new VectorPdfPage(
            Number: 1,
            WidthPoints: 1190.551,
            HeightPoints: 841.89,
            RotationDegrees: 0,
            Entities: []);

        Assert.Equal(420.0, page.WidthMillimetres, 3);
        Assert.Equal(297.0, page.HeightMillimetres, 3);
    }

    [Fact]
    public void Polyline_keeps_every_vertex_in_source_order()
    {
        var polyline = new VectorPolyline(
            "curve-1",
            [new(0, 0), new(5, 2), new(10, 0)],
            false,
            new VectorStyle());

        Assert.Equal(3, polyline.Vertices.Count);
        Assert.Equal(5, polyline.Vertices[1].X);
    }
}
