using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Recognition;

public static class CircularArcDetector
{
    public static bool TryFit(
        IReadOnlyList<Point2> points,
        out CircularArcFit arc,
        double relativeTolerance = 0.01d)
    {
        arc = default;
        if (points is null || points.Count < 5)
            return false;

        var first = points[0];
        var middle = points[points.Count / 2];
        var last = points[points.Count - 1];
        if (!TryCircumcenter(first, middle, last, out var center))
            return false;

        var radius = GeometryMath.Distance(center, first);
        if (radius <= 1e-6)
            return false;
        var tolerance = Math.Max(1e-4d, radius * relativeTolerance);
        if (points.Any(point => Math.Abs(GeometryMath.Distance(center, point) - radius) > tolerance))
            return false;

        var start = Math.Atan2(first.Y - center.Y, first.X - center.X);
        var end = Math.Atan2(last.Y - center.Y, last.X - center.X);
        var middleAngle = Math.Atan2(middle.Y - center.Y, middle.X - center.X);
        var ccwSweep = NormalizePositive(end - start);
        var ccwMiddle = NormalizePositive(middleAngle - start);
        if (ccwMiddle > ccwSweep + 1e-6d)
        {
            (start, end) = (end, start);
            ccwSweep = NormalizePositive(end - start);
        }

        arc = new CircularArcFit(center, radius, start, start + ccwSweep);
        return ccwSweep > 1e-5d && ccwSweep < Math.PI * 2d - 1e-5d;
    }

    private static bool TryCircumcenter(Point2 a, Point2 b, Point2 c, out Point2 center)
    {
        var determinant = 2d * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(determinant) <= 1e-9d)
        {
            center = default;
            return false;
        }

        var aa = a.X * a.X + a.Y * a.Y;
        var bb = b.X * b.X + b.Y * b.Y;
        var cc = c.X * c.X + c.Y * c.Y;
        center = new Point2(
            (aa * (b.Y - c.Y) + bb * (c.Y - a.Y) + cc * (a.Y - b.Y)) / determinant,
            (aa * (c.X - b.X) + bb * (a.X - c.X) + cc * (b.X - a.X)) / determinant);
        return true;
    }

    private static double NormalizePositive(double angle)
    {
        while (angle < 0d) angle += Math.PI * 2d;
        while (angle >= Math.PI * 2d) angle -= Math.PI * 2d;
        return angle;
    }
}

public readonly record struct CircularArcFit(
    Point2 Center,
    double Radius,
    double StartAngleRadians,
    double EndAngleRadians);
