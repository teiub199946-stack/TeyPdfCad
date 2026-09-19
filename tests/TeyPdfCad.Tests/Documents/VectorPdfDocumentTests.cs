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
}
