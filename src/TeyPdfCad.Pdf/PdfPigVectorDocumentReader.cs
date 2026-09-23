using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Tokens;

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
            var rawMediaOrigin = TryGetDirectRawMediaBoxOrigin(sourcePage);
            var graphics = graphicsInterpreter.InterpretDetailed(
                sourcePage.Operations,
                sourcePage.Number,
                sourcePage.Rotation.Value,
                mediaWidth,
                mediaHeight,
                rawMediaOrigin?.Left ?? mediaBounds.Left,
                rawMediaOrigin?.Bottom ?? mediaBounds.Bottom);
            var entities = graphics.Entities.ToList();
            var diagnostics = graphics.Diagnostics.ToList();
            var words = NearestNeighbourWordExtractor.Instance.GetWords(sourcePage.Letters);
            var fontPrograms = PdfEmbeddedFontProgramCatalog.Create(document, sourcePage);
            var textSequence = 0;
            var skippedHiddenText = false;
            var encounteredTextClipping = false;
            var encounteredAmbiguousFontIdentity = false;
            var encounteredMissingAdvanceWidth = false;
            var encounteredMissingVisibleWidth = false;
            var encounteredMissingVisibleHeight = false;
            foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word.Text)))
            {
                textSequence++;

                if (word.Letters.Any(letter => IsTextClippingMode(letter.RenderingMode)))
                    encounteredTextClipping = true;

                if (word.Letters.Any(letter => !IsVisible(letter.RenderingMode)))
                {
                    skippedHiddenText = true;
                    continue;
                }

                var first = word.Letters[0];
                var last = word.Letters[^1];
                var baselineX = last.EndBaseLine.X - first.StartBaseLine.X;
                var baselineY = last.EndBaseLine.Y - first.StartBaseLine.Y;
                if (Math.Abs(baselineX) <= 1e-9 && Math.Abs(baselineY) <= 1e-9)
                {
                    baselineX = first.EndBaseLine.X - first.StartBaseLine.X;
                    baselineY = first.EndBaseLine.Y - first.StartBaseLine.Y;
                }
                var heightPoints = MeasureWordHeightPoints(word);
                var advanceWidthPoints = Math.Sqrt(baselineX * baselineX + baselineY * baselineY);
                var visibleWidthPoints = MeasureVisibleWidthAlongBaseline(
                    word.BoundingBox,
                    baselineX,
                    baselineY,
                    advanceWidthPoints);
                var visibleHeightPoints = MeasureVisibleHeightPerpendicularToBaseline(
                    word.BoundingBox,
                    baselineX,
                    baselineY,
                    advanceWidthPoints);
                var fontName = GetWordFontName(word);
                var fontProgramIdentity = fontPrograms.ResolveUnique(fontName);
                if (string.IsNullOrWhiteSpace(fontName))
                    encounteredAmbiguousFontIdentity = true;
                if (advanceWidthPoints <= 1e-9)
                    encounteredMissingAdvanceWidth = true;
                if (visibleWidthPoints <= 1e-9)
                    encounteredMissingVisibleWidth = true;
                if (visibleHeightPoints <= 1e-9)
                    encounteredMissingVisibleHeight = true;

                entities.Add(new VectorText(
                    SourceId: $"page-{sourcePage.Number}-text-{textSequence}",
                    Value: word.Text,
                    InsertionPoint: new Point2(
                        first.StartBaseLine.X * VectorPdfPage.MillimetresPerPoint,
                        first.StartBaseLine.Y * VectorPdfPage.MillimetresPerPoint),
                    HeightPoints: heightPoints,
                    Style: new VectorStyle(RgbColor: ToRgb(first.Color.ToRGBValues())),
                    RotationRadians: Math.Atan2(baselineY, baselineX),
                    AdvanceWidthPoints: advanceWidthPoints,
                    FontName: fontName,
                    VisualCenter: new Point2(
                        word.BoundingBox.Centroid.X * VectorPdfPage.MillimetresPerPoint,
                        word.BoundingBox.Centroid.Y * VectorPdfPage.MillimetresPerPoint),
                    VisibleWidthPoints: visibleWidthPoints,
                    VisibleHeightPoints: visibleHeightPoints,
                    FontProgramSha256: fontProgramIdentity?.Sha256,
                    FontProgramSubtype: fontProgramIdentity?.FontSubtype,
                    FontEncodingName: fontProgramIdentity?.EncodingName,
                    FontHasToUnicode: fontProgramIdentity?.HasToUnicode,
                    FontIsSubset: fontProgramIdentity?.IsSubset));
            }

            if (skippedHiddenText)
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "hidden-pdf-text-skipped",
                    $"Hidden PDF text was intentionally omitted on page {sourcePage.Number} so it cannot become visible DWG text."));
            }

            if (encounteredTextClipping)
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "unsupported-pdf-text-clipping",
                    $"PDF text participates in a clipping path on page {sourcePage.Number}; visible text is preserved where applicable, but text-defined clipping is not reconstructed."));
            }

            if (encounteredAmbiguousFontIdentity)
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "ambiguous-pdf-text-font",
                    $"One or more visible PDF words on page {sourcePage.Number} do not have one uniform non-empty font identity; text is preserved, but exact font equivalence is not proven."));
            }

            if (encounteredMissingAdvanceWidth)
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "missing-pdf-text-advance",
                    $"One or more visible PDF words on page {sourcePage.Number} have no usable baseline advance width; exact text-width equivalence is not proven."));
            }

            if (encounteredMissingVisibleWidth)
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "missing-pdf-text-visible-width",
                    $"One or more visible PDF words on page {sourcePage.Number} have no usable visible bounding width along the text baseline; exact rendered-width equivalence is not proven."));
            }

            if (encounteredMissingVisibleHeight)
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "missing-pdf-text-visible-height",
                    $"One or more visible PDF words on page {sourcePage.Number} have no usable visible bounding height perpendicular to the text baseline; exact rendered-height equivalence is not proven."));
            }

            pages.Add(new VectorPdfPage(
                Number: sourcePage.Number,
                WidthPoints: sourcePage.Width,
                HeightPoints: sourcePage.Height,
                RotationDegrees: sourcePage.Rotation.Value,
                Entities: entities,
                SourceDiagnostics: diagnostics));
        }

        return Task.FromResult(new VectorPdfDocument(pages));
    }

    private static double MeasureVisibleWidthAlongBaseline(
        PdfRectangle boundingBox,
        double baselineX,
        double baselineY,
        double advanceWidth)
    {
        if (!double.IsFinite(advanceWidth) || advanceWidth <= 1e-9)
            return 0d;

        var unitX = baselineX / advanceWidth;
        var unitY = baselineY / advanceWidth;
        var corners = new[]
        {
            boundingBox.BottomLeft,
            boundingBox.BottomRight,
            boundingBox.TopLeft,
            boundingBox.TopRight
        };

        var projections = corners
            .Select(point => point.X * unitX + point.Y * unitY)
            .ToArray();
        var width = projections.Max() - projections.Min();
        return double.IsFinite(width) && width > 0d ? width : 0d;
    }

    private static double MeasureVisibleHeightPerpendicularToBaseline(
        PdfRectangle boundingBox,
        double baselineX,
        double baselineY,
        double advanceWidth)
    {
        if (!double.IsFinite(advanceWidth) || advanceWidth <= 1e-9)
            return 0d;

        var unitX = baselineX / advanceWidth;
        var unitY = baselineY / advanceWidth;
        var normalX = -unitY;
        var normalY = unitX;
        var corners = new[]
        {
            boundingBox.BottomLeft,
            boundingBox.BottomRight,
            boundingBox.TopLeft,
            boundingBox.TopRight
        };

        var projections = corners
            .Select(point => point.X * normalX + point.Y * normalY)
            .ToArray();
        var height = projections.Max() - projections.Min();
        return double.IsFinite(height) && height > 0d ? height : 0d;
    }

    private static (double Left, double Bottom)? TryGetDirectRawMediaBoxOrigin(
        UglyToad.PdfPig.Content.Page page)
    {
        if (!page.Dictionary.TryGet<ArrayToken>(NameToken.MediaBox, out var mediaBox)
            || mediaBox.Length != 4
            || mediaBox[0] is not NumericToken firstX
            || mediaBox[1] is not NumericToken firstY
            || mediaBox[2] is not NumericToken secondX
            || mediaBox[3] is not NumericToken secondY)
        {
            return null;
        }

        var left = Math.Min(firstX.Double, secondX.Double);
        var bottom = Math.Min(firstY.Double, secondY.Double);
        return double.IsFinite(left) && double.IsFinite(bottom)
            ? (left, bottom)
            : null;
    }

    private static bool IsVisible(TextRenderingMode mode)
        => mode is not TextRenderingMode.Neither
            and not TextRenderingMode.NeitherClip;

    private static bool IsTextClippingMode(TextRenderingMode mode)
        => mode is TextRenderingMode.FillClip
            or TextRenderingMode.StrokeClip
            or TextRenderingMode.FillThenStrokeClip
            or TextRenderingMode.NeitherClip;

    private static string? GetWordFontName(UglyToad.PdfPig.Content.Word word)
    {
        if (word.Letters.Any(letter => string.IsNullOrWhiteSpace(letter.FontName)))
            return null;

        var names = word.Letters
            .Select(letter => letter.FontName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return names.Length == 1 ? names[0] : null;
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
