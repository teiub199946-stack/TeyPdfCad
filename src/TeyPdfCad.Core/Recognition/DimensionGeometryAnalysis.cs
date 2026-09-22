using TeyPdfCad.Core.Compatibility;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Recognition;

internal static class DimensionGeometryAnalysis
{
    private static readonly double[] CanonicalScales = [1, 2, 5, 10, 20, 25, 50, 100, 200, 500];

    public static IEnumerable<LinePrimitive> DimensionLineCandidates(IReadOnlyList<LinePrimitive> lines, TextPrimitive text)
    {
        var localRadius = Math.Max(text.Height * 8.0, 1e-6);
        var localFragments = lines
            .Where(line => GeometryMath.DistancePointToSegment(text.Position, line.Start, line.End) <= localRadius)
            .ToArray();
        var localSet = new HashSet<LinePrimitive>(
            localFragments,
            ReferenceEqualityComparer.Instance);

        foreach (var line in localFragments)
            yield return line;

        // Dimension text is legitimately placed outside the measured span in
        // many drawings. A pure point-to-segment radius misses long dimensions
        // even though the text still lies on the dimension-line extension.
        // Admit only extrapolated candidates that are close to the infinite
        // line and still inside the same conservative projection envelope used
        // by TryAnalyzeGeometry. Arrow + extension evidence remains mandatory.
        foreach (var line in lines)
        {
            if (localSet.Contains(line))
                continue;

            var vector = GeometryMath.Subtract(line.End, line.Start);
            if (GeometryMath.Length(vector) <= 1e-9)
                continue;

            if (GeometryMath.DistancePointToInfiniteLine(
                    text.Position,
                    line.Start,
                    line.End) > localRadius)
                continue;

            var projection = ProjectionParameter(
                text.Position,
                line.Start,
                line.End);
            if (projection is < -0.25 or > 1.25)
                continue;

            yield return line;
        }

        for (var i = 0; i < localFragments.Length; i++)
        for (var j = i + 1; j < localFragments.Length; j++)
            if (TryMergeAcrossText(localFragments[i], localFragments[j], text, out var merged))
                yield return merged;

        // A PDF path can contain a tiny numerical break away from the text.
        // The older merge path intentionally required the two fragments to
        // straddle the text, so outside-text dimensions with a micro-break
        // stayed invisible. Search only text-parallel, near-collinear
        // fragments and only bridge a very small gap; the normal recognizer
        // still requires both extension lines and both arrow/tick endpoints.
        var microMergeFragments = lines
            .Where(line => IsMicroMergeSearchFragment(line, text, localRadius))
            .ToArray();

        for (var i = 0; i < microMergeFragments.Length; i++)
        for (var j = i + 1; j < microMergeFragments.Length; j++)
            if (TryMergeMicroGap(
                    microMergeFragments[i],
                    microMergeFragments[j],
                    text,
                    out var merged))
                yield return merged;
    }

    private static bool TryMergeAcrossText(LinePrimitive first, LinePrimitive second, TextPrimitive text, out LinePrimitive merged)
    {
        merged = first;
        var v1 = GeometryMath.Subtract(first.End, first.Start);
        var v2 = GeometryMath.Subtract(second.End, second.Start);
        if (GeometryMath.Length(v1) <= 1e-9 || GeometryMath.Length(v2) <= 1e-9) return false;

        var u1 = GeometryMath.Normalize(v1);
        var u2 = GeometryMath.Normalize(v2);
        if (Math.Abs(GeometryMath.Dot(u1, u2)) < Math.Cos(2.0 * Math.PI / 180.0)) return false;

        var sameLineTolerance = Math.Max(text.Height * 0.5, 1e-6);
        if (GeometryMath.DistancePointToInfiniteLine(GeometryMath.Midpoint(second.Start, second.End), first.Start, first.End) > sameLineTolerance) return false;
        if (GeometryMath.DistancePointToInfiniteLine(text.Position, first.Start, first.End) > text.Height * 4.0) return false;

        var points = new[] { first.Start, first.End, second.Start, second.End };
        var scalars = points.Select(p => GeometryMath.Dot(GeometryMath.Subtract(p, text.Position), u1)).ToArray();
        var firstCenter = GeometryMath.Dot(GeometryMath.Subtract(GeometryMath.Midpoint(first.Start, first.End), text.Position), u1);
        var secondCenter = GeometryMath.Dot(GeometryMath.Subtract(GeometryMath.Midpoint(second.Start, second.End), text.Position), u1);
        if (firstCenter * secondCenter >= 0) return false;

        var left = scalars.Min();
        var right = scalars.Max();
        var nearLeft = scalars.Where(x => x <= 0).DefaultIfEmpty(double.NegativeInfinity).Max();
        var nearRight = scalars.Where(x => x >= 0).DefaultIfEmpty(double.PositiveInfinity).Min();
        if (!NumericCompat.IsFinite(nearLeft) || !NumericCompat.IsFinite(nearRight)) return false;
        if (nearRight - nearLeft > text.Height * 8.0) return false;

        var leftPoint = points[Array.IndexOf(scalars, left)];
        var rightPoint = points[Array.IndexOf(scalars, right)];
        if (GeometryMath.Distance(leftPoint, rightPoint) <= Math.Max(GeometryMath.Length(v1), GeometryMath.Length(v2))) return false;

        merged = BuildMergedLine(first, second, leftPoint, rightPoint);
        return true;
    }

    private static bool IsMicroMergeSearchFragment(
        LinePrimitive line,
        TextPrimitive text,
        double localRadius)
    {
        var vector = GeometryMath.Subtract(line.End, line.Start);
        var length = GeometryMath.Length(vector);
        if (length <= 1e-9)
            return false;

        var lineAngle = Math.Atan2(vector.Y, vector.X) * 180.0 / Math.PI;
        if (ParallelAngleDifferenceDegrees(lineAngle, text.Rotation) > 15.0)
            return false;

        if (GeometryMath.DistancePointToInfiniteLine(
                text.Position,
                line.Start,
                line.End) > localRadius)
            return false;

        var segmentDistance = GeometryMath.DistancePointToSegment(
            text.Position,
            line.Start,
            line.End);
        return segmentDistance <= Math.Max(localRadius, length * 2.0);
    }

    private static bool TryMergeMicroGap(
        LinePrimitive first,
        LinePrimitive second,
        TextPrimitive text,
        out LinePrimitive merged)
    {
        merged = first;

        var v1 = GeometryMath.Subtract(first.End, first.Start);
        var v2 = GeometryMath.Subtract(second.End, second.Start);
        var length1 = GeometryMath.Length(v1);
        var length2 = GeometryMath.Length(v2);
        if (length1 <= 1e-9 || length2 <= 1e-9)
            return false;

        var u1 = GeometryMath.Normalize(v1);
        var u2 = GeometryMath.Normalize(v2);
        if (Math.Abs(GeometryMath.Dot(u1, u2))
            < Math.Cos(2.0 * Math.PI / 180.0))
            return false;

        var sameLineTolerance = Math.Max(text.Height * 0.10, 0.05);
        if (GeometryMath.DistancePointToInfiniteLine(
                GeometryMath.Midpoint(second.Start, second.End),
                first.Start,
                first.End) > sameLineTolerance)
            return false;

        var origin = first.Start;
        var firstScalars = new[]
        {
            0d,
            GeometryMath.Dot(
                GeometryMath.Subtract(first.End, origin),
                u1)
        };
        var secondScalars = new[]
        {
            GeometryMath.Dot(
                GeometryMath.Subtract(second.Start, origin),
                u1),
            GeometryMath.Dot(
                GeometryMath.Subtract(second.End, origin),
                u1)
        };

        var firstMin = firstScalars.Min();
        var firstMax = firstScalars.Max();
        var secondMin = secondScalars.Min();
        var secondMax = secondScalars.Max();

        var gap = firstMax < secondMin
            ? secondMin - firstMax
            : secondMax < firstMin
                ? firstMin - secondMax
                : 0d;
        if (gap <= 1e-9
            || gap > Math.Max(text.Height * 0.5, 0.5))
            return false;

        var points = new[] { first.Start, first.End, second.Start, second.End };
        var scalars = points
            .Select(point => GeometryMath.Dot(
                GeometryMath.Subtract(point, origin),
                u1))
            .ToArray();
        var leftIndex = Array.IndexOf(scalars, scalars.Min());
        var rightIndex = Array.IndexOf(scalars, scalars.Max());
        var leftPoint = points[leftIndex];
        var rightPoint = points[rightIndex];

        var textProjection = ProjectionParameter(
            text.Position,
            leftPoint,
            rightPoint);
        if (textProjection is < -0.25 or > 1.25)
            return false;

        merged = BuildMergedLine(first, second, leftPoint, rightPoint);
        return true;
    }

    private static LinePrimitive BuildMergedLine(
        LinePrimitive first,
        LinePrimitive second,
        Point2 start,
        Point2 end)
    {
        var sourceIds = first.ProvenanceIds
            .Concat(second.ProvenanceIds)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var strokeWidthMm = first.StrokeWidthMm == second.StrokeWidthMm
            ? first.StrokeWidthMm
            : null;
        var dashPatternMm = first.StrokeDashPattern.SequenceEqual(second.StrokeDashPattern)
            ? first.StrokeDashPattern.ToArray()
            : null;
        var rgbColor = first.RgbColor == second.RgbColor
            ? first.RgbColor
            : null;

        return new LinePrimitive(
            start,
            end,
            first.Layer == second.Layer ? first.Layer : null,
            sourceIds,
            strokeWidthMm,
            dashPatternMm,
            rgbColor);
    }

    private static double ParallelAngleDifferenceDegrees(
        double first,
        double second)
    {
        var difference = Math.Abs(first - second) % 180.0;
        return Math.Min(difference, 180.0 - difference);
    }

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
        return 1.0 - NumericCompat.Clamp(error / 0.03, 0.0, 1.0);
    }

    public static double ArrowEvidence(
        IEnumerable<LinePrimitive> lines,
        LinePrimitive dimensionLine,
        double textHeight,
        Point2 unitDim,
        IReadOnlyCollection<LinePrimitive>? excluded = null)
    {
        var materialized = lines as IReadOnlyList<LinePrimitive> ?? lines.ToArray();
        var excludedSet = excluded is null
            ? null
            : new HashSet<LinePrimitive>(excluded, ReferenceEqualityComparer.Instance);
        var radius = Math.Max(textHeight * 2.0, GeometryMath.Distance(dimensionLine.Start, dimensionLine.End) * 0.04);
        var first = HasArrowLine(materialized, dimensionLine, dimensionLine.Start, radius, textHeight, unitDim, excludedSet);
        var second = HasArrowLine(materialized, dimensionLine, dimensionLine.End, radius, textHeight, unitDim, excludedSet);
        return (Convert.ToInt32(first) + Convert.ToInt32(second)) / 2.0;
    }

    public static IReadOnlyList<LinePrimitive> FindArrowGeometryLines(
        IEnumerable<LinePrimitive> lines,
        LinePrimitive dimensionLine,
        double textHeight,
        Point2 unitDim,
        IReadOnlyCollection<LinePrimitive>? excluded = null)
    {
        var excludedSet = excluded is null
            ? null
            : new HashSet<LinePrimitive>(excluded, ReferenceEqualityComparer.Instance);
        var radius = Math.Max(textHeight * 2.0, GeometryMath.Distance(dimensionLine.Start, dimensionLine.End) * 0.04);
        return lines
            .Where(line => !ReferenceEquals(line, dimensionLine))
            .Where(line => excludedSet is null || !excludedSet.Contains(line))
            .Where(line => IsArrowLineAt(line, dimensionLine.Start, radius, textHeight, unitDim)
                || IsArrowLineAt(line, dimensionLine.End, radius, textHeight, unitDim))
            .Distinct()
            .ToArray();
    }

    private static bool HasArrowLine(
        IEnumerable<LinePrimitive> lines,
        LinePrimitive dimensionLine,
        Point2 endpoint,
        double radius,
        double textHeight,
        Point2 unitDim,
        IReadOnlySet<LinePrimitive>? excluded)
        => lines.Any(line =>
            !ReferenceEquals(line, dimensionLine)
            && (excluded is null || !excluded.Contains(line))
            && IsArrowLineAt(line, endpoint, radius, textHeight, unitDim));

    private static bool IsArrowLineAt(
        LinePrimitive line,
        Point2 endpoint,
        double radius,
        double textHeight,
        Point2 unitDim)
    {
        var length = GeometryMath.Distance(line.Start, line.End);
        if (length <= 1e-9 || length > Math.Max(textHeight * 4.0, radius * 2.0)) return false;
        if (GeometryMath.Distance(GeometryMath.Midpoint(line.Start, line.End), endpoint) > radius) return false;

        // Arrow/tick geometry must actually touch or cross the dimension
        // endpoint (within PDF-import noise tolerance). Merely being a short
        // diagonal nearby is not enough; otherwise wall/grid fragments can
        // masquerade as the second arrow.
        var contactTolerance = Math.Max(textHeight * 0.5d, 0.5d);
        if (GeometryMath.DistancePointToSegment(endpoint, line.Start, line.End) > contactTolerance)
            return false;

        var dot = Math.Abs(GeometryMath.Dot(
            GeometryMath.Normalize(GeometryMath.Subtract(line.End, line.Start)),
            unitDim));
        return dot is > 0.20 and < 0.98;
    }
}
