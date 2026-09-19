using TeyPdfCad.Pdf;
using System.Text;
using Xunit;

namespace TeyPdfCad.Pdf.Tests;

public sealed class PdfPigVectorDocumentReaderTests
{
    [Fact]
    public async Task Reader_preserves_one_boundary_and_one_fill_for_fill_and_stroke_path()
    {
        const string contents = "0 1 0 RG 1 w 0.2 0.4 0.6 rg 10 10 m 110 10 l 110 60 l 10 60 l 10 10 l B";
        await using var input = CreateMinimalPdf(contents);

        var document = await new PdfPigVectorDocumentReader().ReadAsync(input, default);
        var page = Assert.Single(document.Pages);

        var fill = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorFilledPath>());
        Assert.Equal(4, fill.Boundary.Count);
        Assert.Equal(0x336699, fill.Style.RgbColor);
        var boundary = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorPolyline>());
        Assert.True(boundary.IsClosed);
        Assert.Equal(4, boundary.Vertices.Count);
        Assert.Equal(0x00FF00, boundary.Style.RgbColor);
    }

    [Fact]
    public async Task Reader_preserves_closed_fill_and_stroke_operator_and_compound_even_odd_path()
    {
        const string contents = "1 0 0 RG 0 0 1 rg 10 10 m 30 10 l 30 30 l 10 30 l b\n0 1 0 rg 40 10 m 80 10 l 80 50 l 40 50 l h 50 20 m 70 20 l 70 40 l 50 40 l h f*";
        await using var input = CreateMinimalPdf(contents);

        var page = Assert.Single((await new PdfPigVectorDocumentReader().ReadAsync(input, default)).Pages);

        var fills = page.Entities.OfType<TeyPdfCad.Core.Documents.VectorFilledPath>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Contains(fills, fill => fill.Style.RgbColor == 0x0000FF && fill.Boundary.Count == 4);
        var compound = Assert.Single(fills, fill => fill.FillRule == TeyPdfCad.Core.Documents.VectorFillRule.EvenOdd);
        Assert.Single(compound.InteriorBoundaries);
        Assert.Equal(4, compound.InteriorBoundaries[0].Count);
        var stroke = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorPolyline>());
        Assert.True(stroke.IsClosed);
        Assert.Equal(0xFF0000, stroke.Style.RgbColor);
    }

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
        var filled = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorFilledPath>());
        Assert.Equal(4, filled.Boundary.Count);
        Assert.Equal(10 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, filled.Boundary[0].X, 6);
        Assert.Equal(0x336699, filled.Style.RgbColor);
        var text = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorText>());
        Assert.Equal(72 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, text.InsertionPoint.X, 6);
        Assert.Equal(700 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, text.InsertionPoint.Y, 6);
    }

    [Fact]
    public async Task Reader_applies_graphics_state_transform_and_preserves_rectangles_curves_and_closed_strokes()
    {
        const string contents = "q 2 0 0 3 5 7 cm 10 20 30 40 re S Q 0 0 m 0 10 10 10 10 0 c s";
        await using var input = CreateMinimalPdf(contents);

        var page = Assert.Single((await new PdfPigVectorDocumentReader().ReadAsync(input, default)).Pages);

        var paths = page.Entities.OfType<TeyPdfCad.Core.Documents.VectorPolyline>().ToArray();
        Assert.Equal(2, paths.Length);
        var rectangle = paths[0];
        Assert.True(rectangle.IsClosed);
        Assert.Equal(25 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, rectangle.Vertices[0].X, 6);
        Assert.Equal(67 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, rectangle.Vertices[0].Y, 6);
        var curve = paths[1];
        Assert.True(curve.IsClosed);
        Assert.Equal(17, curve.Vertices.Count);
        Assert.Equal(10 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, curve.Vertices[^1].X, 6);
    }

    [Fact]
    public async Task Reader_keeps_separate_words_and_records_page_rotation()
    {
        const string contents = "10 20 m 30 40 l S BT /F1 12 Tf 72 700 Td (A3) Tj 200 0 Td (B4) Tj ET";
        await using var input = CreateMinimalPdf(contents, rotation: 90);

        var page = Assert.Single((await new PdfPigVectorDocumentReader().ReadAsync(input, default)).Pages);

        Assert.Equal(90, page.RotationDegrees);
        Assert.Equal(841.89, page.WidthPoints, 3);
        Assert.Equal(595.276, page.HeightPoints, 3);
        var texts = page.Entities.OfType<TeyPdfCad.Core.Documents.VectorText>().ToArray();
        Assert.Equal(["A3", "B4"], texts.Select(text => text.Value).ToArray());
        Assert.Equal(700 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, texts[0].InsertionPoint.X, 6);
        Assert.Equal((595.276 - 72) * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, texts[0].InsertionPoint.Y, 6);
        Assert.Equal(12d, texts[0].HeightPoints, 6);
        Assert.Equal(-Math.PI / 2d, texts[0].RotationRadians, 6);
        Assert.Equal(0x000000, texts[0].Style.RgbColor);
        var line = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorLine>());
        Assert.Equal(20 * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, line.Start.X, 6);
        Assert.Equal((595.276 - 10) * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, line.Start.Y, 6);
    }

    [Fact]
    public async Task Reader_preserves_gray_color_and_scales_stroke_style_with_the_graphics_transform()
    {
        const string contents = "q 2 0 0 2 0 0 cm 0.1 G 1 w [2 1] 0 d 0 0 m 10 0 l S Q";
        await using var input = CreateMinimalPdf(contents);

        var line = Assert.Single(Assert.Single((await new PdfPigVectorDocumentReader().ReadAsync(input, default)).Pages).Entities.OfType<TeyPdfCad.Core.Documents.VectorLine>());

        Assert.Equal(0x1A1A1A, line.Style.RgbColor);
        Assert.Equal(2d, line.Style.StrokeWidthPoints);
        Assert.Equal([4d, 2d], line.Style.DashPatternPoints);
    }

    [Fact]
    public async Task Reader_reports_unsupported_clipping_instead_of_silently_ignoring_it()
    {
        await using var input = CreateMinimalPdf("0 0 m 20 0 l 10 10 l 20 20 l 0 20 l h W n 1 1 m 10 10 l S");

        var page = Assert.Single((await new PdfPigVectorDocumentReader().ReadAsync(input, default)).Pages);

        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "unsupported-pdf-path-operation");
        Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorLine>());
    }

    [Fact]
    public async Task Reader_clips_vector_lines_to_a_rectangular_clipping_path()
    {
        await using var input = CreateMinimalPdf("0 0 20 20 re W n -10 10 m 30 10 l S");

        var page = Assert.Single((await new PdfPigVectorDocumentReader().ReadAsync(input, default)).Pages);
        var line = Assert.Single(page.Entities.OfType<TeyPdfCad.Core.Documents.VectorLine>());

        Assert.Empty(page.Diagnostics);
        Assert.Equal(0d, line.Start.X, 6);
        Assert.Equal(20d * TeyPdfCad.Core.Documents.VectorPdfPage.MillimetresPerPoint, line.End.X, 6);
    }

    private static MemoryStream CreateMinimalPdf(string? contentsOverride = null, int rotation = 0)
    {
        var contents = contentsOverride ?? "1 0 0 RG 2 w [4 2] 0 d 10 10 m 100 10 l 100 10 m 100 100 l S\n0.2 0.4 0.6 rg 10 10 m 110 10 l 110 60 l 10 60 l h f\nBT /F1 12 Tf 72 700 Td (A3) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.276 841.89] /Rotate {rotation} /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
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
