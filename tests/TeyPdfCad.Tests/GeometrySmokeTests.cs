using TeyPdfCad.Core.Geometry;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class GeometrySmokeTests
{
    [Fact]
    public void Point2_PreservesCoordinates()
    {
        var point = new Point2(12.5, -7.25);
        Assert.Equal(12.5, point.X);
        Assert.Equal(-7.25, point.Y);
    }
}
