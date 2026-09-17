using System.Globalization;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public sealed class LinearDimensionRecognizer
{
    private static readonly double[] CanonicalScales = [1, 2, 5, 10, 20, 25, 50, 100, 200, 500];

    public IReadOnlyList<DimensionCandidate> Recognize(
        PrimitiveScene scene,
        DimensionRecognitionOptions? options = null)
    {
        options ??= new DimensionRecognitionOptions();
        var result = new List<DimensionCandidate>();

        foreach (var text in scene.Texts)
        {
            if (!TryParseDimensionValue(text.Value, out var displayedValue) || displayedValue <= 0)
                continue;

            DimensionCandidate? best = null;
            foreach (var dimensionLine in scene.Lines)
            {
                var candidate = TryBuildCandidate(scene, text, displayedValue, dimensionLine, options);
                if (candidate is not null && (best is null || candidate.Confidence > best.Confidence))
                    best = candidate;
            }

            if (best is not null && best.Confidence >= options.MinConfidence)
                result.Add(best);
        }

        return result;
    }

    private static DimensionCandidate? TryBuildCandidate(
        PrimitiveScene scene,
        TextPrimitive text,
        double displayedValue,
        LinePrimitive dimensionLine,
        DimensionRecognitionOptions options)
    {
        var dimVector = GeometryMath.Subtract(dimensionLine.End, dimensionLine.Start);
        var dimLength = GeometryMath.Length(dimVector);
        if (dimLength <= 1e-9) return null;

        var textTolerance = Math.Max(text.Height * options.TextDistanceHeightMultiplier, 1e-6);
        var textDistance = GeometryMath.DistancePointToInfiniteLine(
            text.Position, dimensionLine.Start, dimensionLine.End);
        if (textDistance > textTolerance) return null;

        var projection = ProjectionParameter(text.Position, dimensionLine.Start, dimensionLine.End);
        if (projection < -0.25 || projection > 1.25) return null;

        var endpointTolerance = Math.Max(
            text.Height * options.EndpointToleranceHeightMultiplier,
            dimLength * 0.02);
        var unitDim = GeometryMath.Normalize(dimVector);
        var perpendicularCosLimit = Math.Sin(options.PerpendicularAngleToleranceDegrees * Math.PI / 180.0);

        var ext1 = FindBestExtensionLine(scene.Lines, dimensionLine, dimensionLine.Start,
            unitDim, endpointTolerance, perpendicularCosLimit);
        var ext2 = FindBestExtensionLine(scene.Lines, dimensionLine, dimensionLine.End,
            unitDim, endpointTolerance, perpendicularCosLimit, ext1);
        if (ext1 is null || ext2 is null) return null;

        var p1 = DefinitionPointFromExtension(ext1, dimensionLine);
        var p2 = DefinitionPointFromExtension(ext2, dimensionLine);
        var projectedDistance = Math.Abs(GeometryMath.Dot(GeometryMath.Subtract(p2, p1), unitDim));
        if (projectedDistance <= 1e-9) return null;

        var rawScale = displayedValue / projectedDistance;
        var scale = options.DrawingScale ?? SnapToCanonicalScale(rawScale, options.CanonicalScaleRelativeTolerance);
        if (scale is null) return null;

        var reconstructed = projectedDistance * scale.Value;
        var relativeError = Math.Abs(reconstructed - displayedValue) / Math.Max(displayedValue, 1.0);
        if (relativeError > options.MeasurementRelativeTolerance) return null;

        var textScore = 1.0 - Math.Clamp(textDistance / textTolerance, 0.0, 1.0);
        var measurementScore = 1.0 - Math.Clamp(relativeError / options.MeasurementRelativeTolerance, 0.0, 1.0);
        var scaleScore = options.DrawingScale.HasValue ? 1.0 : CanonicalScaleScore(rawScale, scale.Value);
        var confidence = 0.20 + 0.20 + 0.20 + 0.10 * textScore + 0.15 * scaleScore + 0.15 * measurementScore;

        var angle = Math.Atan2(dimVector.Y, dimVector.X) * 180.0 / Math.PI;
        var normalized = Math.Abs(NormalizeAngle(angle));
        var axisAligned = Math.Min(normalized, Math.Abs(90.0 - normalized)) <= 1.0;
        var kind = axisAligned ? DimensionKind.Rotated : DimensionKind.Aligned;

        return new DimensionCandidate(
            kind,
            p1,
            p2,
            GeometryMath.Midpoint(dimensionLine.Start, dimensionLine.End),
            displayedValue,
            reconstructed,
            scale.Value,
            Math.Clamp(confidence, 0.0, 1.0),
            text.Value);
    }

    private static LinePrimitive? FindBestExtensionLine(
        IEnumerable<LinePrimitive> lines,
        LinePrimitive dimensionLine,
        Point2 endpoint,
        Point2 unitDim,
        double endpointTolerance,
        double perpendicularCosLimit,
        LinePrimitive? excluded = null)
    {
        LinePrimitive? best = null;
        var bestDistance = double.MaxValue;

        foreach (var line in lines)
        {
            if (ReferenceEquals(line, dimensionLine) || ReferenceEquals(line, excluded)) continue;

            var extVector = GeometryMath.Subtract(line.End, line.Start);
            var extLength = GeometryMath.Length(extVector);
            if (extLength <= 1e-9) continue;

            var unitExt = GeometryMath.Normalize(extVector);
            if (Math.Abs(GeometryMath.Dot(unitDim, unitExt)) > perpendicularCosLimit) continue;

            var distance = GeometryMath.DistancePointToInfiniteLine(endpoint, line.Start, line.End);
            if (distance <= endpointTolerance && distance < bestDistance)
            {
                best = line;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static Point2 DefinitionPointFromExtension(LinePrimitive extension, LinePrimitive dimensionLine)
    {
        var d1 = GeometryMath.DistancePointToInfiniteLine(extension.Start, dimensionLine.Start, dimensionLine.End);
        var d2 = GeometryMath.DistancePointToInfiniteLine(extension.End, dimensionLine.Start, dimensionLine.End);
        return d1 >= d2 ? extension.Start : extension.End;
    }

    private static double ProjectionParameter(Point2 point, Point2 a, Point2 b)
    {
        var ab = GeometryMath.Subtract(b, a);
        var denominator = GeometryMath.Dot(ab, ab);
        if (denominator <= 1e-12) return 0;
        return GeometryMath.Dot(GeometryMath.Subtract(point, a), ab) / denominator;
    }

    private static double? SnapToCanonicalScale(double value, double relativeTolerance)
    {
        var best = CanonicalScales.OrderBy(x => Math.Abs(x - value)).First();
        var error = Math.Abs(best - value) / Math.Max(best, 1e-9);
        return error <= relativeTolerance ? best : null;
    }

    private static double CanonicalScaleScore(double raw, double snapped)
    {
        var error = Math.Abs(raw - snapped) / Math.Max(snapped, 1e-9);
        return 1.0 - Math.Clamp(error / 0.03, 0.0, 1.0);
    }

    private static double NormalizeAngle(double degrees)
    {
        var value = degrees % 180.0;
        if (value < 0) value += 180.0;
        return value > 90.0 ? 180.0 - value : value;
    }

    private static bool TryParseDimensionValue(string value, out double parsed)
    {
        var normalized = value
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }
}
