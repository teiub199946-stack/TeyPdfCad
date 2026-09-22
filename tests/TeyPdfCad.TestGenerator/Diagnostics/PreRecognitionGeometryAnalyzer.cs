using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

public enum PreRecognitionGeometryStatus
{
    Valid,
    Degenerate,
    Crowded
}

public sealed record DegenerateChainSegment(
    int Index,
    double PaperLength,
    double PostMismatchLength,
    string Reason);

public sealed record PreRecognitionGeometryAssessment(
    PreRecognitionGeometryStatus Status,
    IReadOnlyList<DegenerateChainSegment> DegenerateSegments,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Separates certainly impossible synthetic chain evidence from difficult but
/// still geometrically present input. It does not alter expected labels,
/// recognizer thresholds, or release-gate metrics.
/// </summary>
public static class PreRecognitionGeometryAnalyzer
{
    private const double GeometryEpsilon = 1e-9;

    public static PreRecognitionGeometryAssessment Analyze(DimensionCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if (testCase.DimensionType != DimensionType.Chain
            || testCase.Segments.Count == 0)
        {
            return new PreRecognitionGeometryAssessment(
                PreRecognitionGeometryStatus.Valid,
                [],
                []);
        }

        if (!double.IsFinite(testCase.DrawingScale)
            || testCase.DrawingScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(testCase),
                "Drawing scale must be finite and positive.");
        }

        // AddDimensionEvidence shifts both endpoints by one half of this
        // amount when endpoint mismatch is enabled. The calculation remains
        // in paper-space, matching the builder's actual mismatch operation.
        var totalMismatch = testCase.Noise.EndpointMismatch
            ? Math.Max(testCase.Noise.CoordinateJitter, 0.01)
            : 0d;

        var degenerate = new List<DegenerateChainSegment>();
        var warnings = new List<string>();

        for (var index = 0; index < testCase.Segments.Count; index++)
        {
            var segment = testCase.Segments[index];
            var drawingLength = Distance(segment.P1, segment.P2);
            var paperLength = drawingLength / testCase.DrawingScale;
            var postMismatchLength = paperLength - totalMismatch;

            if (postMismatchLength <= GeometryEpsilon)
            {
                degenerate.Add(new DegenerateChainSegment(
                    index,
                    paperLength,
                    postMismatchLength,
                    "post-mismatch dimension-line length is non-positive"));
                continue;
            }

            // A short segment may still be valid: dimension text or arrows can
            // be intentionally placed outside the measured span. Report it as
            // diagnostic context only; never reclassify it as degenerate.
            if (paperLength < testCase.TextHeight / testCase.DrawingScale)
            {
                warnings.Add(
                    $"segment {index} is shorter than text height in paper-space");
            }
        }

        return new PreRecognitionGeometryAssessment(
            degenerate.Count > 0
                ? PreRecognitionGeometryStatus.Degenerate
                : warnings.Count > 0
                    ? PreRecognitionGeometryStatus.Crowded
                    : PreRecognitionGeometryStatus.Valid,
            degenerate,
            warnings);
    }

    private static double Distance(Point2D first, Point2D second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
