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
    public void Groups_SourceAware_Adjacent_Dimensions_With_Small_PdfImport_Node_Jitter()
    {
        var first = WithSourceTextHeight(Create(0, 10, 1000, 5), 2.5);
        var second = WithSourceTextHeight(Create(10.5, 20.5, 1000, 15.5), 2.5);

        var chain = Assert.Single(new DimensionChainDetector().Detect([first, second]));

        Assert.Equal(2, chain.Dimensions.Count);
    }

    [Fact]
    public void Groups_Very_Short_SourceAware_Chain_Members_Using_PaperSpace_Jitter_Tolerance()
    {
        var first = WithSourceTextHeight(Create(0, 0.05, 25, 0.025), 2.5);
        var second = WithSourceTextHeight(Create(0.55, 0.60, 25, 0.575), 2.5);

        var chain = Assert.Single(new DimensionChainDetector().Detect([first, second]));

        Assert.Equal(2, chain.Dimensions.Count);
    }

    [Fact]
    public void Very_Short_SourceAware_Members_Still_Reject_Gap_Beyond_PaperSpace_Jitter_Tolerance()
    {
        var first = WithSourceTextHeight(Create(0, 0.05, 25, 0.025), 2.5);
        var second = WithSourceTextHeight(Create(0.80, 0.85, 25, 0.825), 2.5);

        Assert.Empty(new DimensionChainDetector().Detect([first, second]));
    }

    [Fact]
    public void SourceAware_Tolerance_Does_Not_Group_Distinct_Nearby_Dimensions()
    {
        var first = WithSourceTextHeight(Create(0, 10, 1000, 5), 2.5);
        var second = WithSourceTextHeight(Create(10.75, 20.75, 1000, 15.75), 2.5);

        Assert.Empty(new DimensionChainDetector().Detect([first, second]));
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

    private static DimensionCandidate WithSourceTextHeight(DimensionCandidate candidate, double heightMm)
        => candidate with
        {
            SourceAppearance = new DimensionSourceAppearance(
                new DimensionSourceTextAppearance(
                    candidate.SourceText,
                    candidate.DimensionLinePoint,
                    heightMm,
                    0,
                    null,
                    null,
                    []),
                new DimensionSourceLineAppearance(
                    candidate.DefinitionPoint1,
                    candidate.DefinitionPoint2,
                    null,
                    null,
                    null,
                    [],
                    []),
                [],
                [])
        };

    private static DimensionCandidate Create(double x1, double x2, double value, double lineX)
        => new(
            DimensionKind.Rotated,
            new Point2(x1, 0),
            new Point2(x2, 0),
            new Point2(lineX, 5),
            value, value, 100, 0.95, value.ToString(), 1);
}
