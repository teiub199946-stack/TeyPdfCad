namespace TeyPdfCad.Core.Sheets;

public static class StandardSheetDetector
{
    private static readonly (StandardSheetFormat Format, double LongSide, double ShortSide)[] Formats =
    [
        (StandardSheetFormat.A0, 1189d, 841d),
        (StandardSheetFormat.A1, 841d, 594d),
        (StandardSheetFormat.A2, 594d, 420d),
        (StandardSheetFormat.A3, 420d, 297d),
        (StandardSheetFormat.A4, 297d, 210d),
    ];

    public static SheetFormatDetection Detect(double widthMm, double heightMm, double toleranceMm = 2.0)
    {
        if (!IsFinite(widthMm) || !IsFinite(heightMm) || widthMm <= 0 || heightMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMm), "Sheet bounds must be finite and positive.");
        if (!IsFinite(toleranceMm) || toleranceMm < 0)
            throw new ArgumentOutOfRangeException(nameof(toleranceMm), "Sheet tolerance must be finite and non-negative.");

        var landscape = widthMm >= heightMm;
        var longSide = landscape ? widthMm : heightMm;
        var shortSide = landscape ? heightMm : widthMm;
        var match = Formats.FirstOrDefault(format =>
            Math.Abs(longSide - format.LongSide) <= toleranceMm &&
            Math.Abs(shortSide - format.ShortSide) <= toleranceMm);
        var isStandard = match.Format != StandardSheetFormat.Unknown;

        return new SheetFormatDetection(
            widthMm,
            heightMm,
            isStandard ? match.Format : StandardSheetFormat.Unknown,
            isStandard ? landscape ? SheetOrientation.Landscape : SheetOrientation.Portrait : SheetOrientation.Unknown,
            toleranceMm);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
