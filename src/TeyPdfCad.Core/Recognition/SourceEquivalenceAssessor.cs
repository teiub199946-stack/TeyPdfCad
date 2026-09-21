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
                Add(
                    assessments,
                    SourceReplacementPlanner.GetCandidateKey(candidate, page.Number),
                    "DIMENSION",
                    isComplete: false,
                    "Dimension source appearance is not yet fully represented: source text metrics, arrow geometry/style, extension-line style/offsets and dimension-line appearance are not independently captured before DWG emission.");
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
