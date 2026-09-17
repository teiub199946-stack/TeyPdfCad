namespace TeyPdfCad.Core.Geometry;

public static class GeometryMath
{
    public static double Distance(Point2 a, Point2 b)
        => Math.Sqrt(SquaredDistance(a, b));

    public static double SquaredDistance(Point2 a, Point2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    public static Point2 Midpoint(Point2 a, Point2 b)
        => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    public static double Length(Point2 vector)
        => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    public static Point2 Subtract(Point2 a, Point2 b)
        => new(a.X - b.X, a.Y - b.Y);

    public static double Dot(Point2 a, Point2 b)
        => a.X * b.X + a.Y * b.Y;

    public static Point2 Normalize(Point2 vector)
    {
        var length = Length(vector);
        return length <= 1e-12 ? new Point2(0, 0) : new Point2(vector.X / length, vector.Y / length);
    }

    public static double DistancePointToInfiniteLine(Point2 point, Point2 lineStart, Point2 lineEnd)
    {
        var d = Subtract(lineEnd, lineStart);
        var len = Length(d);
        if (len <= 1e-12) return Distance(point, lineStart);

        var rel = Subtract(point, lineStart);
        return Math.Abs(d.X * rel.Y - d.Y * rel.X) / len;
    }

    public static double DistancePointToSegment(Point2 point, Point2 start, Point2 end)
    {
        var segment = Subtract(end, start);
        var denominator = Dot(segment, segment);
        if (denominator <= 1e-12) return Distance(point, start);

        var t = Math.Clamp(Dot(Subtract(point, start), segment) / denominator, 0.0, 1.0);
        var projection = new Point2(start.X + t * segment.X, start.Y + t * segment.Y);
        return Distance(point, projection);
    }
}
