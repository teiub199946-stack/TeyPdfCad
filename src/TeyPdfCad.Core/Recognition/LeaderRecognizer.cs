using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed class LeaderRecognizer
{
    private const double MinimumShaftLength = 10d;
    private const double EndpointTolerance = 0.5d;
    private const double NativeLeaderThreshold = 0.85d;

    public LeaderRecognitionResult Recognize(PrimitiveScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var leaders = new List<LeaderCandidate>();
        var warnings = new List<SemanticWarning>();
        foreach (var shaft in scene.Lines.Where(line => GeometryMath.Distance(line.Start, line.End) >= MinimumShaftLength))
        {
            var arrows = scene.Lines
                .Where(line => !ReferenceEquals(line, shaft))
                .Where(line => Touches(line, shaft.Start))
                .ToArray();
            var text = scene.Texts
                .OrderBy(candidate => GeometryMath.Distance(candidate.Position, shaft.End))
                .FirstOrDefault();
            var hasNearbyText = text is not null && GeometryMath.Distance(text.Position, shaft.End) <= Math.Max(10d, GeometryMath.Distance(shaft.Start, shaft.End) * 0.25d);
            var hasArrowPair = arrows.Length >= 2 && HasSeparatedArrowLegs(arrows, shaft.Start);
            var confidence = 0.4d + (hasArrowPair ? 0.3d : 0d) + (hasNearbyText ? 0.3d : 0d);

            if (text is not null && confidence >= NativeLeaderThreshold)
            {
                leaders.Add(new LeaderCandidate(
                    shaft.Start,
                    text.Position,
                    text.Value,
                    confidence,
                    shaft.ProvenanceIds.Concat(arrows.SelectMany(arrow => arrow.ProvenanceIds)).Concat(text.ProvenanceIds).Distinct().ToArray()));
            }
            else if (arrows.Length > 0 || hasNearbyText)
            {
                warnings.Add(new SemanticWarning(
                    "leader-low-confidence",
                    "Leader-like geometry remains editable source geometry because arrow and text evidence is incomplete.",
                    shaft.ProvenanceIds.Concat(arrows.SelectMany(arrow => arrow.ProvenanceIds)).Concat(text?.ProvenanceIds ?? []).Distinct().ToArray()));
            }
        }

        return new LeaderRecognitionResult(leaders, warnings);
    }

    private static bool Touches(LinePrimitive line, Point2 point)
        => GeometryMath.Distance(line.Start, point) <= EndpointTolerance || GeometryMath.Distance(line.End, point) <= EndpointTolerance;

    private static bool HasSeparatedArrowLegs(IReadOnlyList<LinePrimitive> arrows, Point2 arrowPoint)
    {
        var directions = arrows
            .Select(arrow => GeometryMath.Distance(arrow.Start, arrowPoint) <= EndpointTolerance
                ? GeometryMath.Normalize(GeometryMath.Subtract(arrow.End, arrow.Start))
                : GeometryMath.Normalize(GeometryMath.Subtract(arrow.Start, arrow.End)))
            .ToArray();

        return directions.SelectMany((first, index) => directions.Skip(index + 1), (first, second) => GeometryMath.Dot(first, second))
            .Any(dot => dot < 0.9d);
    }
}

public sealed record LeaderRecognitionResult(
    IReadOnlyList<LeaderCandidate> NativeLeaders,
    IReadOnlyList<SemanticWarning> Warnings);
