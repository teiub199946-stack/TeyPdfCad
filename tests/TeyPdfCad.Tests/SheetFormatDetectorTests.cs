using TeyPdfCad.Core.Sheets;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class SheetFormatDetectorTests
{
    [Fact]
    public void DetectsLandscapeA3WithinTolerance()
    {
        var result = StandardSheetDetector.Detect(419.2, 297.8);

        Assert.Equal(StandardSheetFormat.A3, result.Format);
        Assert.Equal(SheetOrientation.Landscape, result.Orientation);
    }

    [Fact]
    public void DetectsPortraitA3WithinTolerance()
    {
        var result = StandardSheetDetector.Detect(297, 420);

        Assert.Equal(StandardSheetFormat.A3, result.Format);
        Assert.Equal(SheetOrientation.Portrait, result.Orientation);
    }

    [Fact]
    public void LeavesUnknownFormatFailClosed()
    {
        var result = StandardSheetDetector.Detect(210, 297);

        Assert.Equal(StandardSheetFormat.Unknown, result.Format);
        Assert.Equal(SheetOrientation.Unknown, result.Orientation);
    }
}
