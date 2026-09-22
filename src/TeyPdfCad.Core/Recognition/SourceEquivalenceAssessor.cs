using TeyPdfCad.Core.Documents;
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

        if (appearance.DimensionLine.IsCompositeObservation)
        {
            return new SourceEquivalenceAssessmentResult(
                false,
                "Dimension source appearance is captured, but the dimension-line observation is composite (for example split around text); raw source segments must be compared independently before suppression.");
        }

        return new SourceEquivalenceAssessmentResult(
            false,
            "Dimension source appearance is captured before DWG emission (text metrics/color and line geometry/stroke/dash/color), but native DIMENSION font metrics, arrow topology/type and dimension/extension line style mapping are not yet independently proven equivalent.");
    }

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
