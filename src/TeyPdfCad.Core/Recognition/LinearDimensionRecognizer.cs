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

        if (!options.DrawingScale.HasValue)
        {
            var observations = CollectScaleObservations(scene, options);
            var consensus = ScaleConsensusEstimator.Estimate(
                observations,
                options.ScaleConsensusRelativeTolerance,
                options.ScaleConsensusMinimumVotes);

            if (consensus.HasValue)
            {
                var consensusOptions = options with { DrawingScale = consensus.Value.Scale };
                var consensusResult = RecognizeWithResolvedScale(scene, consensusOptions);

                // A consensus is useful only if it survives strict measurement validation.
                if (consensusResult.Count >= options.ScaleConsensusMinimumVotes)
                    return consensusResult;
            }
        }

        // Conservative fallback: fixed scale when supplied, otherwise canonical scales only.
        return RecognizeWithResolvedScale(scene, options);
    }

    private static IReadOnlyList<ScaleObservation> CollectScaleObservations(
        PrimitiveScene scene,
        DimensionRecognitionOptions options)
    {
        var observations = new List<ScaleObservation>();

        foreach (var text in scene.Texts)
        {
            if (!TryParseDimensionValue(text.Value, out var displayedValue) || displayedValue <= 0)
                continue;

            ScaleObservation? best = null;
            foreach (var dimensionLine in DimensionGeometryAnalysis.DimensionLineCandidates(scene.Lines, text))
            {
                var probe = TryAnalyzeGeometry(scene, text, dimensionLine, options);
                if (probe is null || probe.Value.ProjectedDistance <= 1e-9) continue;

                var rawScale = displayedValue / probe.Value.ProjectedDistance;
                if (!double.IsFinite(rawScale) || rawScale <= 1e-9 || rawScale > 1e9) continue;

                // This weight intentionally excludes measurement agreement: the raw scale is what we are estimating.
                var structuralWeight = 0.60 + 0.25 * probe.Value.TextScore + 0.15 * probe.Value.ArrowEvidence;
                var observation = new ScaleObservation(rawScale, structuralWeight);
                if (best is null || observation.Weight > best.Value.Weight)
                    best = observation;
            }

            // At most one vote per text object prevents one label from dominating via many nearby line candidates.
            if (best.HasValue) observations.Add(best.Value);
        }

        return observations;
    }

    private static IReadOnlyList<DimensionCandidate> RecognizeWithResolvedScale(
        PrimitiveScene scene,
        DimensionRecognitionOptions options)
    {
        var result = new List<DimensionCandidate>();

        foreach (var text in scene.Texts)
        {
            if (!TryParseDimensionValue(text.Value, out var displayedValue) || displayedValue <= 0)
                continue;

            DimensionCandidate? best = null;
            foreach (var dimensionLine in DimensionGeometryAnalysis.DimensionLineCandidates(scene.Lines, text))
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
        var probe = TryAnalyzeGeometry(scene, text, dimensionLine, options);
        if (probe is null) return null;

        var rawScale = displayedValue / probe.Value.ProjectedDistance;
        var scale = DimensionGeometryAnalysis.ResolveScale(
            rawScale,
            options.DrawingScale,
            options.CanonicalScaleRelativeTolerance);
        if (scale is null) return null;

        var reconstructed = probe.Value.ProjectedDistance * scale.Value;
        var relativeError = Math.Abs(reconstructed - displayedValue) / Math.Max(displayedValue, 1.0);
        if (relativeError > options.MeasurementRelativeTolerance) return null;

        var measurementScore = 1.0 - Math.Clamp(relativeError / options.MeasurementRelativeTolerance, 0.0, 1.0);
        var scaleScore = options.DrawingScale.HasValue
            ? measurementScore
            : DimensionGeometryAnalysis.CanonicalScaleScore(rawScale, scale.Value);

        var confidence = 0.50
            + 0.10 * probe.Value.TextScore
            + 0.15 * scaleScore
            + 0.15 * measurementScore
            + 0.10 * probe.Value.ArrowEvidence;

        return new DimensionCandidate(
            probe.Value.Kind,
            probe.Value.DefinitionPoint1,
            probe.Value.DefinitionPoint2,
            probe.Value.DimensionLinePoint,
            displayedValue,
            reconstructed,
            scale.Value,
            Math.Clamp(confidence, 0.0, 1.0),
            text.Value,
            probe.Value.ArrowEvidence);
    }

    private static GeometryProbe? TryAnalyzeGeometry(
        PrimitiveScene scene,
        TextPrimitive text,
        LinePrimitive dimensionLine,
        DimensionRecognitionOptions options)
    {
        var dimVector = GeometryMath.Subtract(dimensionLine.End, dimensionLine.Start);
        var dimLength = GeometryMath.Length(dimVector);
        if (dimLength <= 1e-9) return null;

        var textTolerance = Math.Max(text.Height * options.TextDistanceHeightMultiplier, 1e-6);
        var textDistance = GeometryMath.DistancePointToInfiniteLine(text.Position, dimensionLine.Start, dimensionLine.End);
        if (textDistance > textTolerance) return null;

        var projection = DimensionGeometryAnalysis.ProjectionParameter(text.Position, dimensionLine.Start, dimensionLine.End);
        if (projection < -0.25 || projection > 1.25) return null;

        var endpointTolerance = Math.Max(
            text.Height * options.EndpointToleranceHeightMultiplier,
            dimLength * 0.02);
        var unitDim = GeometryMath.Normalize(dimVector);
        var cosLimit = Math.Sin(options.PerpendicularAngleToleranceDegrees * Math.PI / 180.0);

        var ext1 = DimensionGeometryAnalysis.FindExtensionLine(
            scene.Lines,
            dimensionLine,
            dimensionLine.Start,
            unitDim,
            endpointTolerance,
            cosLimit);
        var ext2 = DimensionGeometryAnalysis.FindExtensionLine(
            scene.Lines,
            dimensionLine,
            dimensionLine.End,
            unitDim,
            endpointTolerance,
            cosLimit,
            ext1);
        if (ext1 is null || ext2 is null) return null;

        var p1 = DimensionGeometryAnalysis.DefinitionPoint(ext1, dimensionLine);
        var p2 = DimensionGeometryAnalysis.DefinitionPoint(ext2, dimensionLine);
        var projectedDistance = Math.Abs(GeometryMath.Dot(GeometryMath.Subtract(p2, p1), unitDim));
        if (projectedDistance <= 1e-9) return null;

        var textScore = 1.0 - Math.Clamp(textDistance / textTolerance, 0.0, 1.0);
        var arrows = DimensionGeometryAnalysis.ArrowEvidence(scene.Lines, dimensionLine, text.Height, unitDim);

        var angle = Math.Atan2(dimVector.Y, dimVector.X) * 180.0 / Math.PI;
        var normalized = NormalizeAngle(angle);
        var axisAligned = Math.Min(normalized, Math.Abs(90.0 - normalized)) <= 1.0;

        return new GeometryProbe(
            axisAligned ? DimensionKind.Rotated : DimensionKind.Aligned,
            p1,
            p2,
            GeometryMath.Midpoint(dimensionLine.Start, dimensionLine.End),
            projectedDistance,
            textScore,
            arrows);
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
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private readonly record struct GeometryProbe(
        DimensionKind Kind,
        Point2 DefinitionPoint1,
        Point2 DefinitionPoint2,
        Point2 DimensionLinePoint,
        double ProjectedDistance,
        double TextScore,
        double ArrowEvidence);
}
