using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Recognition;

internal static class DimensionGeometryAnalysis
{
    private static readonly double[] CanonicalScales = [1, 2, 5, 10, 20, 25, 50, 100, 200, 500];

    public static double ProjectionParameter(Point2 point, Point2 a, Point2 b)
    {
        var ab = GeometryMath.Subtract(b, a);
        var denominator = GeometryMath.Dot(ab, ab);
        return denominator <= 1e-12 ? 0 : GeometryMath.Dot(GeometryMath.Subtract(point, a), ab) / denominator;
    }

    public static LinePrimitive? FindExtensionLine(IEnumerable<LinePrimitive> lines, LinePrimitive dimensionLine,
        Point2 endpoint, Point2 unitDim, double endpointTolerance, double perpendicularCosLimit,
        LinePrimitive? excluded = null)
    {
        LinePrimitive? best = null;
        var bestDistance = double.MaxValue;
        foreach (var line in lines)
        {
            if (ReferenceEquals(line, dimensionLine) || ReferenceEquals(line, excluded)) continue;
            var vector = GeometryMath.Subtract(line.End, line.Start);
            if (GeometryMath.Length(vector) <= 1e-9) continue;
            if (Math.Abs(GeometryMath.Dot(unitDim, GeometryMath.Normalize(vector))) > perpendicularCosLimit) continue;

            var distance = GeometryMath.DistancePointToSegment(endpoint, line.Start, line.End);
            if (distance <= endpointTolerance && distance < bestDistance)
            {
                best = line;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static Point2 DefinitionPoint(LinePrimitive extension, LinePrimitive dimensionLine)
    {
        var startDistance = GeometryMath.DistancePointToInfiniteLine(extension.Start, dimensionLine.Start, dimensionLine.End);
        var endDistance = GeometryMath.DistancePointToInfiniteLine(extension.End, dimensionLine.Start, dimensionLine.End);
        return startDistance >= endDistance ? extension.Start : extension.End;
    }

    public static double? ResolveScale(double rawScale, double? fixedScale, double tolerance)
    {
        if (fixedScale.HasValue) return fixedScale.Value;
        var best = CanonicalScales.OrderBy(x => Math.Abs(x - rawScale)).First();
        var error = Math.Abs(best - rawScale) / Math.Max(best, 1e-9);
        return error <= tolerance ? best : null;
    }

    public static double CanonicalScaleScore(double raw, double snapped)
    {
        var error = Math.Abs(raw - snapped) / Math.Max(snapped, 1e-9);
        return 1.0 - Math.Clamp(error / 0.03, 0.0, 1.0);
    }

    public static double ArrowEvidence(IEnumerable<LinePrimitive> lines, LinePrimitive dimensionLine,
        double textHeight, Point2 unitDim)
    {
        var radius = Math.Max(textHeight * 2.0, GeometryMath.Distance(dimensionLine.Start, dimensionLine.End) * 0.04);
        var first = HasArrowLine(lines, dimensionLine, dimensionLine.Start, radius, textHeight, unitDim);
        var second = HasArrowLine(lines, dimensionLine, dimensionLine.End, radius, textHeight, unitDim);
        return (Convert.ToInt32(first) + Convert.ToInt32(second)) / 2.0;
    }

    private static bool HasArrowLine(IEnumerable<LinePrimitive> lines, LinePrimitive dimensionLine,
        Point2 endpoint, double radius, double textHeight, Point2 unitDim)
    {
        foreach (var line in lines)
        {
            if (ReferenceEquals(line, dimensionLine)) continue;
            var length = GeometryMath.Distance(line.Start, line.End);
            if (length <= 1e-9 || length > Math.Max(textHeight * 4.0, radius * 2.0)) continue;
            if (GeometryMath.Distance(GeometryMath.Midpoint(line.Start, line.End), endpoint) > radius) continue;
            var dot = Math.Abs(GeometryMath.Dot(GeometryMath.Normalize(GeometryMath.Subtract(line.End, line.Start)), unitDim));
            if (dot is > 0.20 and < 0.98) return true;
        }
        return false;
    }
}
