using Xunit;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Tests.Recognition;

public sealed class DimensionChainDetectorTests
{
    [Fact]
    public void Groups_Three_Adjacent_Dimensions_Into_One_Chain()
    {
        var dimensions = new[]
        {
            Create(0, 12, 1200, 6),
            Create(12, 30, 1800, 21),
            Create(30, 52, 2200, 41)
        };

        var chain = Assert.Single(new DimensionChainDetector().Detect(dimensions));
        Assert.Equal(3, chain.Dimensions.Count);
        Assert.Equal(5200, chain.TotalDisplayedValue, 6);
        Assert.Equal(100, chain.DrawingScale, 6);
    }

    [Fact]
    public void Does_Not_Group_Dimensions_On_Different_Dimension_Lines()
    {
        var first = Create(0, 12, 1200, 6);
        var second = new DimensionCandidate(
            DimensionKind.Rotated,
            new Point2(12, 0),
            new Point2(30, 0),
            new Point2(21, 20),
            1800, 1800, 100, 0.95, "1800", 1);

        Assert.Empty(new DimensionChainDetector().Detect([first, second]));
    }

    private static DimensionCandidate Create(double x1, double x2, double value, double lineX)
        => new(
            DimensionKind.Rotated,
            new Point2(x1, 0),
            new Point2(x2, 0),
            new Point2(lineX, 5),
            value, value, 100, 0.95, value.ToString(), 1);
}
