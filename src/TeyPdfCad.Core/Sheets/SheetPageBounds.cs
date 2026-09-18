namespace TeyPdfCad.Core.Sheets;

public sealed record SheetPageBounds(
    double MinX,
    double MinY,
    double WidthMm,
    double HeightMm,
    string Units = "mm",
    double DrawingUnitsPerMm = 1)
{
    public double DrawingWidth => WidthMm * DrawingUnitsPerMm;
    public double DrawingHeight => HeightMm * DrawingUnitsPerMm;
    public double MaxX => MinX + DrawingWidth;
    public double MaxY => MinY + DrawingHeight;
}
