using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig;

namespace TeyPdfCad.Pdf;

public sealed class PdfPigVectorDocumentReader
{
    public Task<VectorPdfDocument> ReadAsync(Stream pdf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        cancellationToken.ThrowIfCancellationRequested();

        using var document = PdfDocument.Open(pdf);
        var pages = new List<VectorPdfPage>();

        foreach (var sourcePage in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entities = new List<VectorEntity>();
            var text = string.Concat(sourcePage.Letters.Select(letter => letter.Value));
            if (!string.IsNullOrWhiteSpace(text))
            {
                var first = sourcePage.Letters[0];
                entities.Add(new VectorText(
                    SourceId: $"page-{sourcePage.Number}-text-1",
                    Value: text,
                    InsertionPoint: new Point2(first.Location.X, first.Location.Y),
                    HeightPoints: first.FontSize,
                    Style: new VectorStyle()));
            }

            pages.Add(new VectorPdfPage(
                Number: sourcePage.Number,
                WidthPoints: sourcePage.Width,
                HeightPoints: sourcePage.Height,
                RotationDegrees: 0,
                Entities: entities));
        }

        return Task.FromResult(new VectorPdfDocument(pages));
    }
}
