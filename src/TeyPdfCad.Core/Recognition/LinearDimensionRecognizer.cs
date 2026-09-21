using TeyPdfCad.Core.Compatibility;
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
            var clusters = ScaleConsensusEstimator.EstimateClusters(
                observations,
                options.ScaleConsensusRelativeTolerance,
                options.ScaleConsensusMinimumVotes);

            if (clusters.Count > 0)
            {
                var combined = new List<DimensionCandidate>();

                foreach (var cluster in clusters)
                {
                    var clusterOptions = options with { DrawingScale = cluster.Scale };
                    var clusterResult = RecognizeWithResolvedScale(scene, clusterOptions);
                    if (clusterResult.Count < options.ScaleConsensusMinimumVotes) continue;

                    foreach (var candidate in clusterResult)
                        AddOrReplaceEquivalent(combined, candidate);
                }

                if (combined.Count > 0)
                {
                    return SelectUniqueCandidates(combined, clusters);
                }
            }
        }

        return RecognizeWithResolvedScale(scene, options);
    }

    private static IReadOnlyList<DimensionCandidate> SelectUniqueCandidates(
        IReadOnlyList<DimensionCandidate> candidates,
        IReadOnlyList<ScaleConsensus> clusters)
    {
        var scaleRank = clusters
            .Select((cluster, index) => new ScaleRankEntry(cluster.Scale, index))
            .ToArray();

        var perText = candidates
            .GroupBy(candidate => candidate.ProvenanceIds.FirstOrDefault(id =>
                    id.StartsWith("page-", StringComparison.Ordinal)
                    && id.IndexOf("-text-", StringComparison.Ordinal) >= 0)
                ?? candidate.SourceText + "|" + Math.Round(candidate.DimensionLinePoint.X, 4) + "|" + Math.Round(candidate.DimensionLinePoint.Y, 4))
            .Select(group => group
                .OrderBy(candidate => ScaleRank(candidate.DrawingScale, scaleRank))
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenByDescending(candidate => candidate.ArrowEvidence)
                .First())
            .ToArray();

        return perText
            .GroupBy(GeometryKey)
            .Select(group => group
                .OrderBy(candidate => ScaleRank(candidate.DrawingScale, scaleRank))
                .ThenByDescending(candidate => candidate.Confidence)
                .First())
            .OrderBy(x => x.DimensionLinePoint.Y)
            .ThenBy(x => x.DimensionLinePoint.X)
            .ToArray();
    }

    private static int ScaleRank(double scale, IReadOnlyList<ScaleRankEntry> ranks)
    {
        for (var index = 0; index < ranks.Count; index++)
            if (Math.Abs(ranks[index].Scale - scale) / Math.Max(scale, 1e-9) <= 0.02)
                return ranks[index].Rank;
        return int.MaxValue;
    }

    private static string GeometryKey(DimensionCandidate candidate)
    {
        var first = candidate.DefinitionPoint1;
        var second = candidate.DefinitionPoint2;
        if (first.X > second.X || (Math.Abs(first.X - second.X) < 1e-6 && first.Y > second.Y))
            (first, second) = (second, first);

        return string.Join("|",
            candidate.Kind,
            Math.Round(first.X, 2),
            Math.Round(first.Y, 2),
            Math.Round(second.X, 2),
            Math.Round(second.Y, 2),
            Math.Round(candidate.DimensionLinePoint.X, 2),
            Math.Round(candidate.DimensionLinePoint.Y, 2),
            Math.Round(candidate.RotationRadians ?? 0d, 4));
    }

    private static IReadOnlyList<ScaleObservation> CollectScaleObservations(
        PrimitiveScene scene,
        DimensionRecognitionOptions options)
    {
        var observations = new List<ScaleObservation>();

        foreach (var text in scene.Texts)
        {
            if (!TryGetLinearDimensionValue(text.Value, out var displayedValue))
                continue;

            ScaleObservation? best = null;
            foreach (var dimensionLine in DimensionGeometryAnalysis.DimensionLineCandidates(scene.Lines, text))
            {
                var probe = TryAnalyzeGeometry(scene, text, dimensionLine, options);
                if (probe is null || probe.Value.ProjectedDistance <= 1e-9) continue;

                var rawScale = displayedValue / probe.Value.ProjectedDistance;
                if (!NumericCompat.IsFinite(rawScale) || rawScale <= 1e-9 || rawScale > 1e9) continue;

                var structuralWeight = 0.60 + 0.25 * probe.Value.TextScore + 0.15 * probe.Value.ArrowEvidence;
                var observation = new ScaleObservation(rawScale, structuralWeight);
                if (best is null || observation.Weight > best.Value.Weight)
                    best = observation;
            }

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
            if (!TryGetLinearDimensionValue(text.Value, out var displayedValue))
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

        var measurementScore = 1.0 - NumericCompat.Clamp(relativeError / options.MeasurementRelativeTolerance, 0.0, 1.0);
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
            NumericCompat.Clamp(confidence, 0.0, 1.0),
            text.Value,
            probe.Value.ArrowEvidence,
            probe.Value.SourcePrimitiveIds)
        {
            RotationRadians = probe.Value.RotationRadians,
            SourceClaims = probe.Value.SourceClaims
        };
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
        var dimensionAngle = Math.Atan2(dimVector.Y, dimVector.X) * 180.0 / Math.PI;
        if (ParallelAngleDifferenceDegrees(dimensionAngle, text.Rotation)
            > options.TextRotationToleranceDegrees)
            return null;

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

        var textScore = 1.0 - NumericCompat.Clamp(textDistance / textTolerance, 0.0, 1.0);
        var arrows = DimensionGeometryAnalysis.ArrowEvidence(scene.Lines, dimensionLine, text.Height, unitDim);
        var arrowLines = DimensionGeometryAnalysis.FindArrowGeometryLines(
            scene.Lines,
            dimensionLine,
            text.Height,
            unitDim);
        var provenanceIds = MergeProvenance(
            text.ProvenanceIds,
            dimensionLine.ProvenanceIds,
            ext1.ProvenanceIds,
            ext2.ProvenanceIds,
            arrowLines.SelectMany(line => line.ProvenanceIds).ToArray());
        var sourceClaims = RecognizerSourceClaimBuilder.FromProvenance(
                SourceUsageRole.DimensionLine,
                dimensionLine.ProvenanceIds)
            .Concat(RecognizerSourceClaimBuilder.FromProvenance(
                SourceUsageRole.ExtensionLine,
                ext1.ProvenanceIds,
                ext2.ProvenanceIds))
            .Concat(RecognizerSourceClaimBuilder.FromProvenance(
                SourceUsageRole.ArrowGeometry,
                arrowLines.SelectMany(line => line.ProvenanceIds).ToArray()))
            .Concat(RecognizerSourceClaimBuilder.FromProvenance(
                SourceUsageRole.Text,
                text.ProvenanceIds))
            .Distinct()
            .ToArray();

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
            arrows,
            provenanceIds,
            sourceClaims,
            Math.Atan2(dimVector.Y, dimVector.X));
    }

    private static IReadOnlyList<string> MergeProvenance(params IReadOnlyList<string>[] groups)
        => groups
            .SelectMany(x => x)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void AddOrReplaceEquivalent(List<DimensionCandidate> dimensions, DimensionCandidate candidate)
    {
        var index = dimensions.FindIndex(existing => SameDimensionGeometry(existing, candidate));
        if (index < 0)
        {
            dimensions.Add(candidate);
            return;
        }

        if (candidate.Confidence > dimensions[index].Confidence)
            dimensions[index] = candidate;
    }

    private static bool SameDimensionGeometry(DimensionCandidate a, DimensionCandidate b)
    {
        if (!string.Equals(a.SourceText, b.SourceText, StringComparison.Ordinal)) return false;

        const double tolerance = 1e-5;
        var direct = GeometryMath.Distance(a.DefinitionPoint1, b.DefinitionPoint1) <= tolerance
            && GeometryMath.Distance(a.DefinitionPoint2, b.DefinitionPoint2) <= tolerance;
        var reversed = GeometryMath.Distance(a.DefinitionPoint1, b.DefinitionPoint2) <= tolerance
            && GeometryMath.Distance(a.DefinitionPoint2, b.DefinitionPoint1) <= tolerance;

        return (direct || reversed)
            && GeometryMath.Distance(a.DimensionLinePoint, b.DimensionLinePoint) <= tolerance;
    }

    private static bool TryGetLinearDimensionValue(string value, out double displayedValue)
    {
        displayedValue = 0;
        if (!DimensionTextParser.TryParse(value, out var parsed) || parsed is null)
            return false;
        if (parsed.Kind != DimensionTextKind.Linear)
            return false;

        displayedValue = parsed.NominalValue;
        return displayedValue > 0;
    }

    private static double NormalizeAngle(double degrees)
    {
        var value = degrees % 180.0;
        if (value < 0) value += 180.0;
        return value > 90.0 ? 180.0 - value : value;
    }

    private static double ParallelAngleDifferenceDegrees(double first, double second)
    {
        var difference = Math.Abs(first - second) % 180d;
        return Math.Min(difference, 180d - difference);
    }

    private readonly record struct GeometryProbe(
        DimensionKind Kind,
        Point2 DefinitionPoint1,
        Point2 DefinitionPoint2,
        Point2 DimensionLinePoint,
        double ProjectedDistance,
        double TextScore,
        double ArrowEvidence,
        IReadOnlyList<string> SourcePrimitiveIds,
        IReadOnlyList<RecognizerSourceClaim> SourceClaims,
        double RotationRadians);

    private readonly record struct ScaleRankEntry(double Scale, int Rank);
}
