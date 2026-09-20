using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class CircularArcDetectorTests
{
    [Fact]
    public void Fits_an_open_tessellated_quarter_circle()
    {
        var points = Enumerable.Range(0, 9)
            .Select(index =>
            {
                var angle = index * Math.PI / 16d;
                return new Point2(10 + 5 * Math.Cos(angle), 20 + 5 * Math.Sin(angle));
            })
            .ToArray();

        var success = CircularArcDetector.TryFit(points, out var arc);

        Assert.True(success);
        Assert.Equal(10, arc.Center.X, 5);
        Assert.Equal(20, arc.Center.Y, 5);
        Assert.Equal(5, arc.Radius, 5);
        Assert.Equal(0, arc.StartAngleRadians, 5);
        Assert.Equal(Math.PI / 2, arc.EndAngleRadians, 5);
    }

    [Fact]
    public void Rejects_a_non_circular_polyline()
    {
        var points = new[] { new Point2(0, 0), new Point2(5, 0), new Point2(8, 3), new Point2(8, 9), new Point2(2, 11) };

        Assert.False(CircularArcDetector.TryFit(points, out _));
    }
}
