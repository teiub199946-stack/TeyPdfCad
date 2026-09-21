namespace TeyPdfCad.Core.Sheets;

public enum StandardSheetFormat
{
    Unknown = 0,
    A3 = 3,
}

public enum SheetOrientation
{
    Unknown = 0,
    Portrait,
    Landscape,
}

public sealed record SheetFormatDetection(
    double WidthMm,
    double HeightMm,
    StandardSheetFormat Format,
    SheetOrientation Orientation,
    double ToleranceMm)
{
    public SheetMetadata ToMetadata() => new(WidthMm, HeightMm, Format, Orientation);
}
