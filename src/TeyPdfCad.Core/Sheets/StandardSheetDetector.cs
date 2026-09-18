namespace TeyPdfCad.Core.Sheets;

public static class StandardSheetDetector
{
    private const double A3LongMm = 420.0;
    private const double A3ShortMm = 297.0;

    public static SheetFormatDetection Detect(double widthMm, double heightMm, double toleranceMm = 2.0)
    {
        if (!IsFinite(widthMm) || !IsFinite(heightMm) || widthMm <= 0 || heightMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMm), "Sheet bounds must be finite and positive.");
        if (!IsFinite(toleranceMm) || toleranceMm < 0)
            throw new ArgumentOutOfRangeException(nameof(toleranceMm), "Sheet tolerance must be finite and non-negative.");

        var landscape = widthMm >= heightMm;
        var longSide = landscape ? widthMm : heightMm;
        var shortSide = landscape ? heightMm : widthMm;
        var isA3 = Math.Abs(longSide - A3LongMm) <= toleranceMm &&
                   Math.Abs(shortSide - A3ShortMm) <= toleranceMm;

        return new SheetFormatDetection(
            widthMm,
            heightMm,
            isA3 ? StandardSheetFormat.A3 : StandardSheetFormat.Unknown,
            isA3 ? landscape ? SheetOrientation.Landscape : SheetOrientation.Portrait : SheetOrientation.Unknown,
            toleranceMm);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
