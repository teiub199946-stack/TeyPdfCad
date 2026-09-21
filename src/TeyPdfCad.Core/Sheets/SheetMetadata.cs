namespace TeyPdfCad.Core.Sheets;

public sealed record SheetMetadata(
    double WidthMm,
    double HeightMm,
    StandardSheetFormat Format,
    SheetOrientation Orientation)
{
    public SheetPageBounds? PageBounds { get; init; }
}
