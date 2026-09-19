using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace TeyPdfCad.Pdf;

public sealed class PdfPigVectorDocumentReader
{
    public Task<VectorPdfDocument> ReadAsync(Stream pdf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        cancellationToken.ThrowIfCancellationRequested();

        using var document = PdfDocument.Open(pdf);
        var graphicsInterpreter = new PdfGraphicsOperationInterpreter();
        var pages = new List<VectorPdfPage>();

        foreach (var sourcePage in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entities = graphicsInterpreter.Interpret(sourcePage.Operations, sourcePage.Number).ToList();
            var words = NearestNeighbourWordExtractor.Instance.GetWords(sourcePage.Letters);
            var textSequence = 0;
            foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word.Text)))
            {
                var first = word.Letters[0];
                textSequence++;
                entities.Add(new VectorText(
                    SourceId: $"page-{sourcePage.Number}-text-{textSequence}",
                    Value: word.Text,
                    InsertionPoint: new Point2(
                        first.StartBaseLine.X * VectorPdfPage.MillimetresPerPoint,
                        first.StartBaseLine.Y * VectorPdfPage.MillimetresPerPoint),
                    HeightPoints: word.Letters.Max(letter => letter.FontSize),
                    Style: new VectorStyle()));
            }

            pages.Add(new VectorPdfPage(
                Number: sourcePage.Number,
                WidthPoints: sourcePage.Width,
                HeightPoints: sourcePage.Height,
                RotationDegrees: sourcePage.Rotation.Value,
                Entities: entities));
        }

        return Task.FromResult(new VectorPdfDocument(pages));
    }
}
