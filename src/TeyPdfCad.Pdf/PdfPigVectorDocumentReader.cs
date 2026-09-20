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

            var mediaBounds = sourcePage.MediaBox.Bounds;
            var mediaWidth = sourcePage.Rotation.SwapsAxis ? mediaBounds.Height : mediaBounds.Width;
            var mediaHeight = sourcePage.Rotation.SwapsAxis ? mediaBounds.Width : mediaBounds.Height;
            var graphics = graphicsInterpreter.InterpretDetailed(
                sourcePage.Operations,
                sourcePage.Number,
                sourcePage.Rotation.Value,
                mediaWidth,
                mediaHeight,
                mediaBounds.Left,
                mediaBounds.Bottom);
            var entities = graphics.Entities.ToList();
            var words = NearestNeighbourWordExtractor.Instance.GetWords(sourcePage.Letters);
            var textSequence = 0;
            foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word.Text)))
            {
                var first = word.Letters[0];
                var last = word.Letters[^1];
                var baselineX = last.EndBaseLine.X - first.StartBaseLine.X;
                var baselineY = last.EndBaseLine.Y - first.StartBaseLine.Y;
                if (Math.Abs(baselineX) <= 1e-9 && Math.Abs(baselineY) <= 1e-9)
                {
                    baselineX = first.EndBaseLine.X - first.StartBaseLine.X;
                    baselineY = first.EndBaseLine.Y - first.StartBaseLine.Y;
                }
                textSequence++;
                var heightPoints = MeasureWordHeightPoints(word);
                entities.Add(new VectorText(
                    SourceId: $"page-{sourcePage.Number}-text-{textSequence}",
                    Value: word.Text,
                    InsertionPoint: new Point2(
                        first.StartBaseLine.X * VectorPdfPage.MillimetresPerPoint,
                        first.StartBaseLine.Y * VectorPdfPage.MillimetresPerPoint),
                    HeightPoints: heightPoints,
                    Style: new VectorStyle(RgbColor: ToRgb(first.Color.ToRGBValues())),
                    RotationRadians: Math.Atan2(baselineY, baselineX)));
            }

            pages.Add(new VectorPdfPage(
                Number: sourcePage.Number,
                WidthPoints: sourcePage.Width,
                HeightPoints: sourcePage.Height,
                RotationDegrees: sourcePage.Rotation.Value,
                Entities: entities,
                SourceDiagnostics: graphics.Diagnostics));
        }

        return Task.FromResult(new VectorPdfDocument(pages));
    }

    private static int ToRgb((double R, double G, double B) color)
        => ((int)Math.Round(Math.Clamp(color.R, 0d, 1d) * 255d) << 16)
            | ((int)Math.Round(Math.Clamp(color.G, 0d, 1d) * 255d) << 8)
            | (int)Math.Round(Math.Clamp(color.B, 0d, 1d) * 255d);

    // PDF `Letter.PointSize` is the nominal font size declared by the page, which
    // regularly overshoots the visible glyph height (measured ~0.45-0.7x in real
    // drawings), so using it as AutoCAD TEXT Height inflates text and causes
    // overlapping headers/white spots. The glyph bounding box is the faithful
    // source of the rendered height (ascender + descender), so prefer it and only
    // fall back to the nominal size when the glyph boxes are unavailable.
    private static double MeasureWordHeightPoints(UglyToad.PdfPig.Content.Word word)
    {
        var glyphHeights = word.Letters
            .Select(letter => letter.GlyphRectangle.Height)
            .Where(height => height > 1e-6)
            .ToArray();

        if (glyphHeights.Length == 0)
            return word.Letters.Max(letter => letter.PointSize);

        return glyphHeights.Max();
    }
}
