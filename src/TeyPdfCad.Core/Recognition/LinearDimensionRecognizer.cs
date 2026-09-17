using System.Globalization;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public sealed class LinearDimensionRecognizer
{
    public IReadOnlyList<DimensionCandidate> Recognize(PrimitiveScene scene, DimensionRecognitionOptions? options = null)
    {
        options ??= new DimensionRecognitionOptions();
        var result = new List<DimensionCandidate>();

        foreach (var text in scene.Texts)
        {
            if (!TryParseDimensionValue(text.Value, out var value) || value <= 0) continue;
            DimensionCandidate? best = null;
            foreach (var line in DimensionGeometryAnalysis.DimensionLineCandidates(scene.Lines, text))
            {
                var candidate = TryBuildCandidate(scene, text, value, line, options);
                if (candidate is not null && (best is null || candidate.Confidence > best.Confidence)) best = candidate;
            }
            if (best is not null && best.Confidence >= options.MinConfidence) result.Add(best);
        }
        return result;
    }

    private static DimensionCandidate? TryBuildCandidate(PrimitiveScene scene, TextPrimitive text,
        double displayedValue, LinePrimitive dimensionLine, DimensionRecognitionOptions options)
    {
        var dimVector = GeometryMath.Subtract(dimensionLine.End, dimensionLine.Start);
        var dimLength = GeometryMath.Length(dimVector);
        if (dimLength <= 1e-9) return null;

        var textTolerance = Math.Max(text.Height * options.TextDistanceHeightMultiplier, 1e-6);
        var textDistance = GeometryMath.DistancePointToInfiniteLine(text.Position, dimensionLine.Start, dimensionLine.End);
        if (textDistance > textTolerance) return null;
        var projection = DimensionGeometryAnalysis.ProjectionParameter(text.Position, dimensionLine.Start, dimensionLine.End);
        if (projection < -0.25 || projection > 1.25) return null;

        var endpointTolerance = Math.Max(text.Height * options.EndpointToleranceHeightMultiplier, dimLength * 0.02);
        var unitDim = GeometryMath.Normalize(dimVector);
        var cosLimit = Math.Sin(options.PerpendicularAngleToleranceDegrees * Math.PI / 180.0);
        var ext1 = DimensionGeometryAnalysis.FindExtensionLine(scene.Lines, dimensionLine, dimensionLine.Start,
            unitDim, endpointTolerance, cosLimit);
        var ext2 = DimensionGeometryAnalysis.FindExtensionLine(scene.Lines, dimensionLine, dimensionLine.End,
            unitDim, endpointTolerance, cosLimit, ext1);
        if (ext1 is null || ext2 is null) return null;

        var p1 = DimensionGeometryAnalysis.DefinitionPoint(ext1, dimensionLine);
        var p2 = DimensionGeometryAnalysis.DefinitionPoint(ext2, dimensionLine);
        var projectedDistance = Math.Abs(GeometryMath.Dot(GeometryMath.Subtract(p2, p1), unitDim));
        if (projectedDistance <= 1e-9) return null;

        var rawScale = displayedValue / projectedDistance;
        var scale = DimensionGeometryAnalysis.ResolveScale(rawScale, options.DrawingScale, options.CanonicalScaleRelativeTolerance);
        if (scale is null) return null;
        var reconstructed = projectedDistance * scale.Value;
        var relativeError = Math.Abs(reconstructed - displayedValue) / Math.Max(displayedValue, 1.0);
        if (relativeError > options.MeasurementRelativeTolerance) return null;

        var textScore = 1.0 - Math.Clamp(textDistance / textTolerance, 0.0, 1.0);
        var measurementScore = 1.0 - Math.Clamp(relativeError / options.MeasurementRelativeTolerance, 0.0, 1.0);
        var scaleScore = options.DrawingScale.HasValue ? 1.0 : DimensionGeometryAnalysis.CanonicalScaleScore(rawScale, scale.Value);
        var arrows = DimensionGeometryAnalysis.ArrowEvidence(scene.Lines, dimensionLine, text.Height, unitDim);
        var confidence = 0.50 + 0.10 * textScore + 0.15 * scaleScore + 0.15 * measurementScore + 0.10 * arrows;

        var angle = Math.Atan2(dimVector.Y, dimVector.X) * 180.0 / Math.PI;
        var normalized = NormalizeAngle(angle);
        var axisAligned = Math.Min(normalized, Math.Abs(90.0 - normalized)) <= 1.0;
        return new DimensionCandidate(axisAligned ? DimensionKind.Rotated : DimensionKind.Aligned,
            p1, p2, GeometryMath.Midpoint(dimensionLine.Start, dimensionLine.End), displayedValue,
            reconstructed, scale.Value, Math.Clamp(confidence, 0.0, 1.0), text.Value, arrows);
    }

    private static double NormalizeAngle(double degrees)
    {
        var value = degrees % 180.0;
        if (value < 0) value += 180.0;
        return value > 90.0 ? 180.0 - value : value;
    }

    private static bool TryParseDimensionValue(string value, out double parsed)
    {
        var normalized = value.Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }
}
