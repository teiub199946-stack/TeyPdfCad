namespace TeyPdfCad.Core.Sheets;

public enum StandardSheetFormat
{
    Unknown = 0,
    A0,
    A1,
    A2,
    A3 = 3,
    A4,
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
