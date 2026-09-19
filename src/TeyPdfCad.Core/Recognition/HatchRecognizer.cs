using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed class HatchRecognizer
{
    public HatchRecognitionResult Recognize(IReadOnlyList<VectorEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var hatches = new List<HatchCandidate>();
        foreach (var filledPath in entities.OfType<VectorFilledPath>())
        {
            if (HasValidBoundary(filledPath.Boundary))
            {
                hatches.Add(new HatchCandidate(
                    filledPath.Boundary,
                    IsSolid: true,
                    PatternAngleRadians: null,
                    PatternSpacingMillimetres: null,
                    filledPath.Style,
                    filledPath.Confidence,
                    [filledPath.SourceId]));
            }
        }

        var warnings = new List<SemanticWarning>();
        if (hatches.Count == 0 && entities.OfType<VectorLine>().Take(3).Count() == 3)
        {
            warnings.Add(new SemanticWarning(
                "hatch-low-confidence",
                "Parallel source lines remain editable geometry because no closed filled boundary proves a hatch.",
                entities.OfType<VectorLine>().Select(line => line.SourceId).ToArray()));
        }

        return new HatchRecognitionResult(hatches, warnings);
    }

    private static bool HasValidBoundary(IReadOnlyList<Point2> boundary)
        => boundary.Count >= 3 && boundary.Distinct().Count() >= 3;
}

public sealed record HatchRecognitionResult(
    IReadOnlyList<HatchCandidate> NativeHatches,
    IReadOnlyList<SemanticWarning> Warnings);
