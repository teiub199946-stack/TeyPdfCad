using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics.Dimensions;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed record SourceEquivalenceAssessment(
    string CandidateId,
    string SemanticType,
    bool IsComplete,
    string Reason);

public sealed record SourceEquivalenceAssessmentSet(
    IReadOnlyDictionary<string, SourceEquivalenceAssessment> Candidates)
{
    public SourceEquivalenceAssessment GetRequired(string candidateId)
        => Candidates.TryGetValue(candidateId, out var assessment)
            ? assessment
            : throw new InvalidOperationException(
                $"No pre-write source-equivalence assessment exists for candidate {candidateId}.");
}

public static class SourceEquivalenceAssessor
{
    public static SourceEquivalenceAssessmentSet Build(
        VectorPdfPage page,
        SemanticReconstructionResult? semantics,
        HatchRecognitionResult hatchRecognition)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(hatchRecognition);

        var assessments = new Dictionary<string, SourceEquivalenceAssessment>(
            StringComparer.Ordinal);

        if (semantics is not null)
        {
            foreach (var candidate in semantics.Dimensions)
            {
                var dimensionAssessment = AssessDimensionSourceEquivalence(page, candidate);
                Add(
                    assessments,
                    SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                    "DIMENSION",
                    dimensionAssessment.IsComplete,
                    dimensionAssessment.Reason);
            }

            foreach (var candidate in semantics.Leaders)
            {
                Add(
                    assessments,
                    SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                    "LEADER",
                    isComplete: false,
                    "Leader source appearance is not yet fully represented: source text height/style, shaft segmentation/landing geometry, arrow geometry/style and line appearance are incomplete.");
            }

            foreach (var candidate in semantics.Axes)
            {
                Add(
                    assessments,
                    SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                    "AXIS",
                    isComplete: false,
                    "Axis source appearance is not yet equivalent to the synthesized native block: original dash pattern, lineweight/color and segmented geometry are not independently proven against TEY_AXIS.");
            }

            foreach (var candidate in semantics.Levels)
            {
                Add(
                    assessments,
                    SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                    "LEVEL",
                    isComplete: false,
                    "Level source appearance is not yet fully represented: marker contour, leader/stem geometry, source text metrics/style and their relation to TEY_LEVEL are incomplete.");
            }

            foreach (var candidate in semantics.ArcDimensions)
            {
                Add(
                    assessments,
                    SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                    "ARC_DIMENSION",
                    isComplete: false,
                    "Arc-dimension source appearance is not yet fully represented: source arrows, extension geometry, text metrics/style and dimension-line style are incomplete.");
            }
        }

        foreach (var candidate in hatchRecognition.NativeHatches.Where(candidate => !candidate.IsSolid))
        {
            Add(
                assessments,
                SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                "HATCH",
                isComplete: false,
                "Pattern HATCH source equivalence is not yet complete: exact pattern phase/origin, source stroke appearance and boundary-to-pattern visual equivalence are not independently proven.");
        }

        return new SourceEquivalenceAssessmentSet(assessments);
    }

    private static SourceEquivalenceAssessmentResult AssessDimensionSourceEquivalence(
        VectorPdfPage page,
        TeyPdfCad.Core.Semantics.Dimensions.DimensionCandidate candidate)
    {
        if (candidate.SourceAppearance is null)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension has no pre-write source-appearance snapshot; destructive suppression remains blocked.");
        }

        var appearance = candidate.SourceAppearance;
        var evidenceIds = appearance.DimensionLine.SourceIds
            .Concat(appearance.ExtensionLines.SelectMany(line => line.SourceIds))
            .Concat(appearance.ArrowLines.SelectMany(line => line.SourceIds))
            .Concat(appearance.Text.SourceIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var claimedIds = candidate.SourceClaims
            .Where(claim => claim.State == SourceClaimState.Valid && !claim.IsPartial)
            .Select(claim => claim.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (!evidenceIds.SequenceEqual(claimedIds, StringComparer.Ordinal))
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source-appearance evidence does not cover exactly the recognizer-owned source claim set.");
        }

        if (candidate.SourceClaims.Any(claim =>
                claim.State != SourceClaimState.Valid
                || claim.IsPartial
                || string.IsNullOrWhiteSpace(claim.SourceId)))
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source-equivalence requires every recognizer source claim to be valid, non-partial and source-bound.");
        }

        var appearanceRoleBindings = appearance.DimensionLine.SourceIds
            .Select(sourceId => new SourceRoleBinding(sourceId, SourceUsageRole.DimensionLine))
            .Concat(appearance.ExtensionLines.SelectMany(line =>
                line.SourceIds.Select(sourceId => new SourceRoleBinding(sourceId, SourceUsageRole.ExtensionLine))))
            .Concat(appearance.ArrowLines.SelectMany(line =>
                line.SourceIds.Select(sourceId => new SourceRoleBinding(sourceId, SourceUsageRole.ArrowGeometry))))
            .Concat(appearance.Text.SourceIds.Select(sourceId =>
                new SourceRoleBinding(sourceId, SourceUsageRole.Text)))
            .Where(binding => !string.IsNullOrWhiteSpace(binding.SourceId))
            .Distinct()
            .OrderBy(binding => binding.SourceId, StringComparer.Ordinal)
            .ThenBy(binding => binding.Role)
            .ToArray();
        var claimRoleBindings = candidate.SourceClaims
            .Select(claim => new SourceRoleBinding(claim.SourceId, claim.Role))
            .Distinct()
            .OrderBy(binding => binding.SourceId, StringComparer.Ordinal)
            .ThenBy(binding => binding.Role)
            .ToArray();

        if (!appearanceRoleBindings.SequenceEqual(claimRoleBindings))
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source-appearance role bindings do not exactly match recognizer-owned source roles.");
        }

        var pageSourceIds = page.Entities
            .Select(entity => entity.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (evidenceIds.Any(id => !pageSourceIds.Contains(id)))
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source-appearance evidence references a SourceId absent from the source page.");
        }

        var rawEvidenceMismatch = ValidateDimensionSourceAppearance(page, appearance);
        if (rawEvidenceMismatch is not null)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                rawEvidenceMismatch);
        }

        var semanticMismatch = ValidateDimensionCandidateConsistency(candidate, appearance);
        if (semanticMismatch is not null)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                semanticMismatch);
        }

        var styleRepresentabilityMismatch = ValidateDimensionNativeStyleRepresentability(appearance);
        if (styleRepresentabilityMismatch is not null)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                styleRepresentabilityMismatch);
        }

        var arrowTopologyMismatch = ValidateDimensionArrowEndpointEvidence(appearance);
        if (arrowTopologyMismatch is not null)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                arrowTopologyMismatch);
        }

        if (appearance.DimensionLine.IsCompositeObservation)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source appearance is captured and source-linked, but the dimension-line observation is composite (for example split around text); raw source segments must be compared independently before suppression.");
        }

        var remainingBlockers = new[]
        {
            DescribeUnprovenDimensionTextMapping(appearance),
            DescribeUnprovenDimensionArrowMapping(appearance)
        }
        .Where(reason => !string.IsNullOrWhiteSpace(reason))
        .ToArray();

        return new SourceEquivalenceAssessmentResult(
            false,
            string.Join(" ", remainingBlockers));
    }

    private static string? ValidateDimensionNativeStyleRepresentability(
        DimensionSourceAppearance appearance)
    {
        if (appearance.ExtensionLines.Count != 2)
            return null;

        if (!LineAppearanceStyleEqual(
                appearance.ExtensionLines[0],
                appearance.ExtensionLines[1]))
        {
            return "Dimension source extension lines use different visual styles that one native DIMSTYLE cannot represent exactly.";
        }

        foreach (var arrow in appearance.ArrowLines)
        {
            if (!LineAppearanceStyleEqual(appearance.DimensionLine, arrow))
            {
                return "Dimension source arrow visual style differs from the dimension-line style; native DIMENSION arrow appearance would inherit a different DIMSTYLE contract.";
            }
        }

        var lineAppearances = new[]
            {
                appearance.DimensionLine,
                appearance.ExtensionLines[0],
                appearance.ExtensionLines[1]
            }
            .Concat(appearance.ArrowLines)
            .ToArray();

        foreach (var line in lineAppearances)
        {
            if (!line.StrokeWidthMm.HasValue
                || !IsExactlyRepresentableAutoCadLineWeight(line.StrokeWidthMm.Value))
            {
                var value = line.StrokeWidthMm?.ToString(
                    "R",
                    System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>";
                return $"Dimension source lineweight {value} mm is not exactly representable by a standard native AutoCAD LineWeight; nearest-value substitution is forbidden for source equivalence.";
            }
        }

        if (lineAppearances.Any(line => line.DashPatternMm.Count > 0))
        {
            return "Dimension source uses a dashed line pattern, but PDF dash phase is not captured in the source-appearance snapshot; exact native linetype equivalence remains fail-closed until dash phase is preserved.";
        }

        var firstExtension = ExtensionBeyondDimensionLine(
            appearance.ExtensionLines[0],
            appearance.DimensionLine);
        var secondExtension = ExtensionBeyondDimensionLine(
            appearance.ExtensionLines[1],
            appearance.DimensionLine);
        if (!firstExtension.HasValue || !secondExtension.HasValue)
        {
            return "Dimension source extension geometry does not cross/touch the dimension line in a form that one native DIMSTYLE can reproduce exactly.";
        }

        if (!AlmostEqual(firstExtension.Value, secondExtension.Value))
        {
            return "Dimension source extension lines do not extend the same distance beyond the dimension line; one native DIMSTYLE ExtensionLineExtension cannot reproduce both exactly.";
        }

        return null;
    }

    private static bool IsExactlyRepresentableAutoCadLineWeight(double millimetres)
    {
        if (!double.IsFinite(millimetres) || millimetres < 0d)
            return false;

        double[] supported =
        [
            0.00d, 0.05d, 0.09d, 0.13d, 0.15d, 0.18d, 0.20d, 0.25d,
            0.30d, 0.35d, 0.40d, 0.50d, 0.53d, 0.60d, 0.70d, 0.80d,
            0.90d, 1.00d, 1.06d, 1.20d, 1.40d, 1.58d, 2.00d, 2.11d
        ];

        return supported.Any(value => Math.Abs(value - millimetres) <= 1e-6);
    }

    private static double? ExtensionBeyondDimensionLine(
        DimensionSourceLineAppearance extension,
        DimensionSourceLineAppearance dimensionLine)
    {
        var dimensionVector = GeometryMath.Subtract(dimensionLine.End, dimensionLine.Start);
        var extensionVector = GeometryMath.Subtract(extension.End, extension.Start);
        var dimensionLength = GeometryMath.Length(dimensionVector);
        if (dimensionLength <= 1e-9 || GeometryMath.Length(extensionVector) <= 1e-9)
            return null;

        var first = SignedDistance(extension.Start, dimensionLine.Start, dimensionVector, dimensionLength);
        var second = SignedDistance(extension.End, dimensionLine.Start, dimensionVector, dimensionLength);
        if (!double.IsFinite(first) || !double.IsFinite(second))
            return null;

        if (Math.Abs(first) <= 1e-9 || Math.Abs(second) <= 1e-9)
            return 0d;

        if (Math.Sign(first) == Math.Sign(second))
            return null;

        return Math.Min(Math.Abs(first), Math.Abs(second));
    }

    private static double SignedDistance(
        Point2 point,
        Point2 lineStart,
        Point2 lineVector,
        double lineLength)
    {
        var relative = GeometryMath.Subtract(point, lineStart);
        return (lineVector.X * relative.Y - lineVector.Y * relative.X) / lineLength;
    }

    private static bool LineAppearanceStyleEqual(
        DimensionSourceLineAppearance first,
        DimensionSourceLineAppearance second)
    {
        if (first.RgbColor != second.RgbColor)
            return false;

        if (first.StrokeWidthMm.HasValue != second.StrokeWidthMm.HasValue)
            return false;
        if (first.StrokeWidthMm.HasValue
            && !AlmostEqual(first.StrokeWidthMm.Value, second.StrokeWidthMm!.Value))
            return false;

        if (first.DashPatternMm.Count != second.DashPatternMm.Count)
            return false;
        for (var index = 0; index < first.DashPatternMm.Count; index++)
        {
            if (!AlmostEqual(first.DashPatternMm[index], second.DashPatternMm[index]))
                return false;
        }

        return true;
    }

    private static string? ValidateDimensionArrowEndpointEvidence(
        DimensionSourceAppearance appearance)
    {
        if (appearance.ArrowLines.Count < 2)
        {
            return "Dimension source arrow topology is incomplete: both dimension endpoints require independent raw arrow evidence.";
        }

        var firstEndpoint = appearance.DimensionLine.Start;
        var secondEndpoint = appearance.DimensionLine.End;
        var contactTolerance = Math.Max(appearance.Text.HeightMm * 0.5d, 0.5d);
        var firstSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var secondSourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var arrow in appearance.ArrowLines)
        {
            var touchesFirst = GeometryMath.DistancePointToSegment(
                firstEndpoint,
                arrow.Start,
                arrow.End) <= contactTolerance;
            var touchesSecond = GeometryMath.DistancePointToSegment(
                secondEndpoint,
                arrow.Start,
                arrow.End) <= contactTolerance;

            if (touchesFirst && touchesSecond)
            {
                return "Dimension source arrow topology is ambiguous: one raw arrow geometry reaches both dimension endpoints.";
            }

            if (!touchesFirst && !touchesSecond)
            {
                return "Dimension source arrow topology is not endpoint-bound to the source dimension line.";
            }

            var target = touchesFirst ? firstSourceIds : secondSourceIds;
            foreach (var sourceId in arrow.SourceIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                target.Add(sourceId);
        }

        if (firstSourceIds.Count == 0 || secondSourceIds.Count == 0)
        {
            return "Dimension source arrow topology is incomplete: both dimension endpoints require source-bound arrow geometry.";
        }

        if (firstSourceIds.Overlaps(secondSourceIds))
        {
            return "Dimension source arrow topology is ambiguous: the same SourceId claims arrow evidence at both endpoints.";
        }

        return null;
    }

    private static string DescribeUnprovenDimensionTextMapping(
        DimensionSourceAppearance appearance)
    {
        var sourceFont = appearance.Text.FontName ?? "<unknown>";
        var sourceWidth = appearance.Text.AdvanceWidthMm.HasValue
            ? appearance.Text.AdvanceWidthMm.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : "<unknown>";

        return $"Native DIMENSION text is now deterministically bound to TEYPDFCAD_TEXT/arial.ttf, but raw PDF font '{sourceFont}' has not been proven to resolve to the same font binary/glyph metrics, and native rendered advance width has not been independently measured against the source advance width {sourceWidth} mm.";
    }

    private static string? DescribeUnprovenDimensionArrowMapping(
        DimensionSourceAppearance appearance)
    {
        var firstEndpoint = appearance.DimensionLine.Start;
        var secondEndpoint = appearance.DimensionLine.End;
        var contactTolerance = Math.Max(appearance.Text.HeightMm * 0.5d, 0.5d);
        var first = new List<DimensionSourceLineAppearance>();
        var second = new List<DimensionSourceLineAppearance>();

        foreach (var arrow in appearance.ArrowLines)
        {
            var touchesFirst = GeometryMath.DistancePointToSegment(
                firstEndpoint, arrow.Start, arrow.End) <= contactTolerance;
            var touchesSecond = GeometryMath.DistancePointToSegment(
                secondEndpoint, arrow.Start, arrow.End) <= contactTolerance;
            if (touchesFirst == touchesSecond)
                return null;
            (touchesFirst ? first : second).Add(arrow);
        }

        var firstTopology = ClassifyArrowEndpointTopology(firstEndpoint, first, appearance.Text.HeightMm);
        var secondTopology = ClassifyArrowEndpointTopology(secondEndpoint, second, appearance.Text.HeightMm);
        if (firstTopology != secondTopology)
        {
            return $"Dimension source arrow topology differs between endpoints ({firstTopology} vs {secondTopology}); native arrow mapping remains fail-closed.";
        }

        return firstTopology switch
        {
            DimensionArrowEndpointTopology.CrossingObliqueStroke =>
                "Dimension source arrows are endpoint-crossing oblique strokes, but ACadSharp/AutoCAD DIMSTYLE TickSize visual-length calibration against the raw PDF stroke is not yet independently proven.",
            DimensionArrowEndpointTopology.EndpointSingleWing =>
                "Dimension source arrows are endpoint single-wing linework; no native DIMENSION arrow type has been independently proven visually equivalent to this source topology.",
            DimensionArrowEndpointTopology.MultiStroke =>
                "Dimension source arrows use multi-stroke linework; open-V/dot/filled-arrow topology has not yet been independently mapped to a native DIMENSION arrow type.",
            _ =>
                "Dimension source arrow topology/type cannot yet be independently mapped to the native DIMENSION style."
        };
    }

    private static DimensionArrowEndpointTopology ClassifyArrowEndpointTopology(
        Point2 endpoint,
        IReadOnlyList<DimensionSourceLineAppearance> lines,
        double textHeight)
    {
        if (lines.Count == 0)
            return DimensionArrowEndpointTopology.Unknown;
        if (lines.Count > 1)
            return DimensionArrowEndpointTopology.MultiStroke;

        var line = lines[0];
        var length = GeometryMath.Distance(line.Start, line.End);
        if (length <= 1e-9)
            return DimensionArrowEndpointTopology.Unknown;

        var endpointTolerance = Math.Max(Math.Min(textHeight * 0.05d, 0.1d), 1e-6);
        var startDistance = GeometryMath.Distance(endpoint, line.Start);
        var endDistance = GeometryMath.Distance(endpoint, line.End);
        var segmentDistance = GeometryMath.DistancePointToSegment(endpoint, line.Start, line.End);
        if (segmentDistance > endpointTolerance)
            return DimensionArrowEndpointTopology.Unknown;

        var endpointAtTerminal = startDistance <= endpointTolerance || endDistance <= endpointTolerance;
        if (endpointAtTerminal)
            return DimensionArrowEndpointTopology.EndpointSingleWing;

        var projection = ProjectionParameter(endpoint, line.Start, line.End);
        return projection is > 0.05d and < 0.95d
            ? DimensionArrowEndpointTopology.CrossingObliqueStroke
            : DimensionArrowEndpointTopology.Unknown;
    }

    private static double ProjectionParameter(Point2 point, Point2 start, Point2 end)
    {
        var vector = GeometryMath.Subtract(end, start);
        var denominator = GeometryMath.Dot(vector, vector);
        if (denominator <= 1e-12)
            return 0d;
        return GeometryMath.Dot(GeometryMath.Subtract(point, start), vector) / denominator;
    }

    private enum DimensionArrowEndpointTopology
    {
        Unknown,
        EndpointSingleWing,
        CrossingObliqueStroke,
        MultiStroke
    }

    private static string? ValidateDimensionCandidateConsistency(
        DimensionCandidate candidate,
        DimensionSourceAppearance appearance)
    {
        if (!string.Equals(candidate.SourceText, appearance.Text.Value, StringComparison.Ordinal))
        {
            return "Dimension semantic text differs from the raw source appearance text.";
        }

        if (string.IsNullOrWhiteSpace(appearance.Text.FontName))
        {
            return "Dimension source font identity is unavailable; native font equivalence cannot be proven.";
        }

        if (!appearance.Text.AdvanceWidthMm.HasValue
            || !double.IsFinite(appearance.Text.AdvanceWidthMm.Value)
            || appearance.Text.AdvanceWidthMm.Value <= 0d)
        {
            return "Dimension source text advance width is unavailable; native text-metric equivalence cannot be proven.";
        }

        if (!appearance.Text.VisualCenter.HasValue)
        {
            return "Dimension source visual text center is unavailable; native text-placement equivalence cannot be proven.";
        }

        if (!DimensionTextParser.TryParse(appearance.Text.Value, out var parsed)
            || parsed is null
            || parsed.Kind != DimensionTextKind.Linear
            || !AlmostEqual(parsed.NominalValue, candidate.DisplayedValue))
        {
            return "Dimension semantic text/value is not exactly derived from the raw source appearance text.";
        }

        if (appearance.ExtensionLines.Count != 2)
        {
            return "Dimension semantic geometry is not source-equivalent: exactly two source extension-line observations are required.";
        }

        var dimensionVector = GeometryMath.Subtract(
            appearance.DimensionLine.End,
            appearance.DimensionLine.Start);
        if (GeometryMath.Length(dimensionVector) <= 1e-9)
        {
            return "Dimension semantic geometry is not source-equivalent: source dimension line is degenerate.";
        }

        var sourceDimensionMidpoint = GeometryMath.Midpoint(
            appearance.DimensionLine.Start,
            appearance.DimensionLine.End);
        if (!PointEqual(candidate.DimensionLinePoint, sourceDimensionMidpoint))
        {
            return "Dimension semantic geometry differs from the source-appearance dimension-line location.";
        }

        var sourceRotation = Math.Atan2(dimensionVector.Y, dimensionVector.X);
        if (!candidate.RotationRadians.HasValue
            || !ParallelAngleEqual(candidate.RotationRadians.Value, sourceRotation))
        {
            return "Dimension semantic geometry differs from the source-appearance dimension-line rotation.";
        }

        var normalizedDegrees = Math.Abs(sourceRotation * 180d / Math.PI) % 180d;
        if (normalizedDegrees > 90d)
            normalizedDegrees = 180d - normalizedDegrees;
        var axisDistance = Math.Min(normalizedDegrees, Math.Abs(90d - normalizedDegrees));
        var expectedKind = axisDistance <= 1d
            ? DimensionKind.Rotated
            : DimensionKind.Aligned;
        if (candidate.Kind != expectedKind)
        {
            return "Dimension semantic geometry kind differs from the source-appearance orientation.";
        }

        if (!TryDefinitionPoint(appearance.ExtensionLines[0], appearance.DimensionLine, out var sourceDefinition1)
            || !TryDefinitionPoint(appearance.ExtensionLines[1], appearance.DimensionLine, out var sourceDefinition2))
        {
            return "Dimension semantic geometry is not source-equivalent: source extension-line definition endpoint is ambiguous.";
        }

        var direct = PointEqual(candidate.DefinitionPoint1, sourceDefinition1)
            && PointEqual(candidate.DefinitionPoint2, sourceDefinition2);
        var reversed = PointEqual(candidate.DefinitionPoint1, sourceDefinition2)
            && PointEqual(candidate.DefinitionPoint2, sourceDefinition1);
        if (!direct && !reversed)
        {
            return "Dimension semantic geometry definition points differ from the source-appearance extension lines.";
        }

        if (!double.IsFinite(candidate.DrawingScale) || candidate.DrawingScale <= 0d)
        {
            return "Dimension semantic geometry has an invalid drawing scale.";
        }

        var unitDimension = GeometryMath.Normalize(dimensionVector);
        var projectedDistance = Math.Abs(GeometryMath.Dot(
            GeometryMath.Subtract(sourceDefinition2, sourceDefinition1),
            unitDimension));
        var sourceReconstructedMeasurement = projectedDistance * candidate.DrawingScale;
        if (!double.IsFinite(sourceReconstructedMeasurement)
            || !AlmostEqual(sourceReconstructedMeasurement, candidate.ReconstructedMeasurement))
        {
            return "Dimension semantic geometry/reconstructed measurement differs from the source-appearance geometry and scale.";
        }

        return null;
    }

    private static bool TryDefinitionPoint(
        DimensionSourceLineAppearance extension,
        DimensionSourceLineAppearance dimensionLine,
        out Point2 definitionPoint)
    {
        var startDistance = GeometryMath.DistancePointToInfiniteLine(
            extension.Start,
            dimensionLine.Start,
            dimensionLine.End);
        var endDistance = GeometryMath.DistancePointToInfiniteLine(
            extension.End,
            dimensionLine.Start,
            dimensionLine.End);

        if (Math.Abs(startDistance - endDistance) <= 1e-9)
        {
            definitionPoint = default;
            return false;
        }

        definitionPoint = startDistance > endDistance
            ? extension.Start
            : extension.End;
        return true;
    }

    private static bool ParallelAngleEqual(double firstRadians, double secondRadians)
    {
        var difference = Math.Abs(firstRadians - secondRadians) % Math.PI;
        difference = Math.Min(difference, Math.PI - difference);
        return difference <= 1e-9;
    }

    private static string? ValidateDimensionSourceAppearance(
        VectorPdfPage page,
        TeyPdfCad.Core.Semantics.Dimensions.DimensionSourceAppearance appearance)
    {
        if (appearance.Text.SourceIds.Count != 1)
            return "Dimension source text appearance is not bound to exactly one raw source entity.";

        var textSources = page.Entities
            .Where(entity => string.Equals(
                entity.SourceId,
                appearance.Text.SourceIds[0],
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (textSources.Length != 1 || textSources[0] is not VectorText sourceText)
            return "Dimension source text appearance is not independently traceable to exactly one raw VectorText entity.";

        if (!string.Equals(sourceText.Value, appearance.Text.Value, StringComparison.Ordinal)
            || !PointEqual(sourceText.InsertionPoint, appearance.Text.Position)
            || !AlmostEqual(
                sourceText.HeightPoints * VectorPdfPage.MillimetresPerPoint,
                appearance.Text.HeightMm)
            || !AlmostEqual(
                sourceText.RotationRadians * 180d / Math.PI,
                appearance.Text.RotationDegrees)
            || !string.Equals(sourceText.Style.SourceLayer, appearance.Text.Layer, StringComparison.Ordinal)
            || sourceText.Style.RgbColor != appearance.Text.RgbColor
            || !string.Equals(sourceText.FontName, appearance.Text.FontName, StringComparison.Ordinal)
            || !NullableAlmostEqual(
                sourceText.AdvanceWidthPoints > 0d
                    ? sourceText.AdvanceWidthPoints * VectorPdfPage.MillimetresPerPoint
                    : null,
                appearance.Text.AdvanceWidthMm)
            || !NullablePointEqual(sourceText.VisualCenter, appearance.Text.VisualCenter))
        {
            return "Dimension source text appearance differs from the raw VectorPdfPage evidence.";
        }

        var lineAppearances = new[]
            {
                appearance.DimensionLine
            }
            .Concat(appearance.ExtensionLines)
            .Concat(appearance.ArrowLines);

        foreach (var lineAppearance in lineAppearances)
        {
            if (lineAppearance.SourceIds.Count != 1)
            {
                // A composite dimension-line observation is handled explicitly
                // by the caller. Other composite line observations cannot yet
                // be tied to one raw source segment without sub-entity identity.
                if (ReferenceEquals(lineAppearance, appearance.DimensionLine)
                    && appearance.DimensionLine.IsCompositeObservation)
                    continue;

                return "Dimension source line appearance is not bound to exactly one raw source entity.";
            }

            var sourceMatches = page.Entities
                .Where(entity => string.Equals(
                    entity.SourceId,
                    lineAppearance.SourceIds[0],
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (sourceMatches.Length != 1)
                return "Dimension source line appearance is not independently traceable to exactly one raw source entity.";

            var source = sourceMatches[0];
            if (source is VectorPolyline)
            {
                return "Dimension source line appearance references a VectorPolyline source; P0 whole-source suppression cannot prove that unrelated polyline segments are replaced.";
            }

            if (!LineGeometryMatches(source, lineAppearance)
                || !LineStyleMatches(source.Style, lineAppearance))
            {
                return "Dimension source line appearance differs from the raw VectorPdfPage evidence.";
            }
        }

        return null;
    }

    private static bool LineGeometryMatches(
        VectorEntity source,
        TeyPdfCad.Core.Semantics.Dimensions.DimensionSourceLineAppearance appearance)
        => source is VectorLine line
            && SegmentEqual(
                line.Start,
                line.End,
                appearance.Start,
                appearance.End);

    private static bool LineStyleMatches(
        VectorStyle source,
        TeyPdfCad.Core.Semantics.Dimensions.DimensionSourceLineAppearance appearance)
    {
        if (!string.Equals(source.SourceLayer, appearance.Layer, StringComparison.Ordinal)
            || source.RgbColor != appearance.RgbColor)
            return false;

        var sourceStrokeWidth = source.StrokeWidthPoints.HasValue
            ? source.StrokeWidthPoints.Value * VectorPdfPage.MillimetresPerPoint
            : (double?)null;
        if (!NullableAlmostEqual(sourceStrokeWidth, appearance.StrokeWidthMm))
            return false;

        var sourceDash = source.DashPatternPoints?
            .Select(value => value * VectorPdfPage.MillimetresPerPoint)
            .ToArray() ?? [];
        if (sourceDash.Length != appearance.DashPatternMm.Count)
            return false;

        for (var index = 0; index < sourceDash.Length; index++)
            if (!AlmostEqual(sourceDash[index], appearance.DashPatternMm[index]))
                return false;

        return true;
    }

    private static bool SegmentEqual(
        TeyPdfCad.Core.Geometry.Point2 firstStart,
        TeyPdfCad.Core.Geometry.Point2 firstEnd,
        TeyPdfCad.Core.Geometry.Point2 secondStart,
        TeyPdfCad.Core.Geometry.Point2 secondEnd)
        => (PointEqual(firstStart, secondStart) && PointEqual(firstEnd, secondEnd))
            || (PointEqual(firstStart, secondEnd) && PointEqual(firstEnd, secondStart));

    private static bool PointEqual(
        TeyPdfCad.Core.Geometry.Point2 first,
        TeyPdfCad.Core.Geometry.Point2 second)
        => AlmostEqual(first.X, second.X)
            && AlmostEqual(first.Y, second.Y);

    private static bool NullablePointEqual(Point2? first, Point2? second)
        => first.HasValue == second.HasValue
            && (!first.HasValue || PointEqual(first.Value, second!.Value));

    private static bool NullableAlmostEqual(double? first, double? second)
        => first.HasValue == second.HasValue
            && (!first.HasValue || AlmostEqual(first.Value, second!.Value));

    private static bool AlmostEqual(double first, double second)
        => Math.Abs(first - second) <= 1e-6;

    private readonly record struct SourceRoleBinding(
        string SourceId,
        SourceUsageRole Role);

    private readonly record struct SourceEquivalenceAssessmentResult(
        bool IsComplete,
        string Reason);

    private static void Add(
        IDictionary<string, SourceEquivalenceAssessment> output,
        string candidateId,
        string semanticType,
        bool isComplete,
        string reason)
    {
        if (!output.TryAdd(
                candidateId,
                new SourceEquivalenceAssessment(
                    candidateId,
                    semanticType,
                    isComplete,
                    reason)))
        {
            throw new InvalidOperationException(
                $"Duplicate pre-write source-equivalence assessment for candidate {candidateId}.");
        }
    }
}
