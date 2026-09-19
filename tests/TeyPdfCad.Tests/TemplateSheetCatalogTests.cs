using TeyPdfCad.Core.Sheets;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class TemplateSheetCatalogTests
{
    [Theory]
    [InlineData(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape)]
    [InlineData(297, 420, StandardSheetFormat.A3, SheetOrientation.Portrait)]
    [InlineData(210, 297, StandardSheetFormat.A4, SheetOrientation.Portrait)]
    public void Detects_standard_sheet_formats_in_both_orientations(
        double width,
        double height,
        StandardSheetFormat expectedFormat,
        SheetOrientation expectedOrientation)
    {
        var actual = StandardSheetDetector.Detect(width, height);

        Assert.Equal(expectedFormat, actual.Format);
        Assert.Equal(expectedOrientation, actual.Orientation);
    }
}
