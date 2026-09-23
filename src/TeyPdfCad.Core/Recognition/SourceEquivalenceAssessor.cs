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

        if (appearance.DimensionLine.IsCompositeObservation)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source appearance is captured and source-linked, but the dimension-line observation is composite (for example split around text); raw source segments must be compared independently before suppression.");
        }

        return new SourceEquivalenceAssessmentResult(
            false,
            "Dimension source appearance is captured before DWG emission (text metrics/color and line geometry/stroke/dash/color), but native DIMENSION font metrics, arrow topology/type and dimension/extension line style mapping are not yet independently proven equivalent.");
    }

    private static string? ValidateDimensionCandidateConsistency(
        DimensionCandidate candidate,
        DimensionSourceAppearance appearance)
    {
        if (!string.Equals(candidate.SourceText, appearance.Text.Value, StringComparison.Ordinal))
        {
            return "Dimension semantic text differs from the raw source appearance text.";
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
            || !string.Equals(sourceText.FontName, appearance.Text.FontName, StringComparison.Ordinal))
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
