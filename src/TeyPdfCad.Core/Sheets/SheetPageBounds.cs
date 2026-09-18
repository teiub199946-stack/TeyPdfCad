namespace TeyPdfCad.Core.Sheets;

public sealed record SheetPageBounds(
    double MinX,
    double MinY,
    double WidthMm,
    double HeightMm,
    string Units = "mm",
    double DrawingUnitsPerMm = 1)
{
    /// <summary>
    /// Width of the physical page expressed in the drawing coordinate system.
    /// The default scale of one preserves the original millimetre-as-drawing-unit contract.
    /// </summary>
    public double DrawingWidth => WidthMm * DrawingUnitsPerMm;

    /// <summary>
    /// Height of the physical page expressed in the drawing coordinate system.
    /// </summary>
    public double DrawingHeight => HeightMm * DrawingUnitsPerMm;

    public double MaxX => MinX + DrawingWidth;
    public double MaxY => MinY + DrawingHeight;
}
