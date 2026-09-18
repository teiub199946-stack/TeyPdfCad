using TeyPdfCad.Core.Sheets;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class SheetPageBoundsTests
{
    [Fact]
    public void Default_scale_preserves_millimetre_as_drawing_unit_behavior()
    {
        var bounds = new SheetPageBounds(10, 20, 420, 297);

        Assert.Equal(1, bounds.DrawingUnitsPerMm);
        Assert.Equal(430, bounds.MaxX);
        Assert.Equal(317, bounds.MaxY);
        Assert.Equal(420, bounds.DrawingWidth);
        Assert.Equal(297, bounds.DrawingHeight);
    }

    [Fact]
    public void Scaled_page_bounds_convert_physical_a3_dimensions_to_drawing_extents()
    {
        var bounds = new SheetPageBounds(
            MinX: 2829,
            MinY: 2065,
            WidthMm: 420,
            HeightMm: 297,
            DrawingUnitsPerMm: 100);

        Assert.Equal(42000, bounds.DrawingWidth);
        Assert.Equal(29700, bounds.DrawingHeight);
        Assert.Equal(44829, bounds.MaxX);
        Assert.Equal(31765, bounds.MaxY);
    }
}
