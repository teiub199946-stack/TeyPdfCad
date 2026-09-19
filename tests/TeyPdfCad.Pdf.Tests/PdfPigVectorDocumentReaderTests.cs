using TeyPdfCad.Pdf;
using System.Text;
using Xunit;

namespace TeyPdfCad.Pdf.Tests;

public sealed class PdfPigVectorDocumentReaderTests
{
    [Fact]
    public async Task Reader_preserves_page_size_and_positioned_text()
    {
        await using var input = CreateMinimalPdf();

        var document = await new PdfPigVectorDocumentReader().ReadAsync(input, default);

        var page = Assert.Single(document.Pages);
        Assert.Equal(595.276, page.WidthPoints, 3);
        Assert.Equal(841.89, page.HeightPoints, 3);
        Assert.Contains(page.Entities, entity => entity is TeyPdfCad.Core.Documents.VectorText { Value: "A3" });
        var lines = page.Entities.OfType<TeyPdfCad.Core.Documents.VectorLine>().ToArray();
        Assert.Equal(2, lines.Length);
        var line = lines[0];
        Assert.Equal(10 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, line.Start.X, 6);
        Assert.Equal(10 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, line.Start.Y, 6);
        Assert.Equal(2, line.Style.StrokeWidthPoints);
        Assert.Equal([4d, 2d], line.Style.DashPatternPoints);
        Assert.Equal(0xFF0000, line.Style.RgbColor);
        var text = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorText>());
        Assert.Equal(72 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, text.InsertionPoint.X, 6);
        Assert.Equal(700 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, text.InsertionPoint.Y, 6);
    }

    private static MemoryStream CreateMinimalPdf()
    {
        const string contents = "1 0 0 RG 2 w [4 2] 0 d 10 10 m 100 10 l 100 100 l S\nBT /F1 12 Tf 72 700 Td (A3) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.276 841.89] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contents)} >>\nstream\n{contents}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return new MemoryStream(Encoding.ASCII.GetBytes(builder.ToString()));
    }
}
