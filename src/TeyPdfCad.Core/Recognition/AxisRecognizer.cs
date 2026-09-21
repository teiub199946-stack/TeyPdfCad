using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed class AxisRecognizer
{
    private const double MinimumAxisLength = 20d;
    private const double NativeAxisThreshold = 0.85d;

    public AxisRecognitionResult Recognize(PrimitiveScene scene)
    {
        if (scene is null)
        {
            throw new ArgumentNullException(nameof(scene));
        }

        var axes = new List<AxisCandidate>();
        var warnings = new List<SemanticWarning>();
        var consumed = new HashSet<LinePrimitive>();
        foreach (var line in scene.Lines.Where(line => GeometryMath.Distance(line.Start, line.End) >= MinimumAxisLength))
        {
            var layerEvidence = line.Layer?.IndexOf("ОС", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.55d : 0d;
            var dashEvidence = line.StrokeDashPattern.Count >= 3 ? 0.45d : 0d;
            var confidence = layerEvidence + dashEvidence;
            if (confidence >= NativeAxisThreshold)
            {
                axes.Add(new AxisCandidate(line.Start, line.End, confidence, line.ProvenanceIds)
                {
                    SourceClaims = RecognizerSourceClaimBuilder.FromProvenance(
                        SourceUsageRole.AxisGeometry,
                        line.ProvenanceIds)
                });
                consumed.Add(line);
            }
        }

        foreach (var grouped in FindSegmentedAxes(scene.Lines.Where(line => !consumed.Contains(line)).ToArray()))
        {
            axes.Add(grouped.Candidate);
            foreach (var line in grouped.Lines)
                consumed.Add(line);
        }

        foreach (var line in scene.Lines.Where(line =>
                     !consumed.Contains(line)
                     && GeometryMath.Distance(line.Start, line.End) >= MinimumAxisLength))
        {
            var layerEvidence = line.Layer?.IndexOf("ОС", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.55d : 0d;
            var dashEvidence = line.StrokeDashPattern.Count >= 3 ? 0.45d : 0d;
            if (layerEvidence + dashEvidence <= 0d)
                continue;

            warnings.Add(new SemanticWarning(
                "axis-low-confidence",
                "Axis-like line remains editable geometry because the available axis evidence is incomplete.",
                line.ProvenanceIds));
        }

        return new AxisRecognitionResult(axes, warnings);
    }

    private static IReadOnlyList<SegmentedAxisGroup> FindSegmentedAxes(IReadOnlyList<LinePrimitive> lines)
    {
        var groups = new List<SegmentedAxisGroup>();
        var used = new HashSet<LinePrimitive>();
        foreach (var seed in lines.OrderByDescending(line => GeometryMath.Distance(line.Start, line.End)))
        {
            if (used.Contains(seed)) continue;
            var direction = CanonicalDirection(seed);
            var compatible = lines
                .Where(line => !used.Contains(line))
                .Where(line => Math.Abs(GeometryMath.Dot(direction, CanonicalDirection(line))) >= 0.999d)
                .Where(line => GeometryMath.DistancePointToInfiniteLine(line.Start, seed.Start, seed.End) <= 0.25d)
                .Select(line => Project(line, seed.Start, direction))
                .OrderBy(item => item.Minimum)
                .ToArray();

            for (var index = 0; index <= compatible.Length - 3; index++)
            {
                var chain = new List<ProjectedLine> { compatible[index] };
                for (var next = index + 1; next < compatible.Length; next++)
                {
                    var gap = compatible[next].Minimum - chain[chain.Count - 1].Maximum;
                    if (gap < -0.25d) continue;
                    if (gap > 12d) break;
                    chain.Add(compatible[next]);
                }

                if (chain.Count < 3) continue;
                var lengths = chain.Select(item => item.Maximum - item.Minimum).OrderBy(value => value).ToArray();
                if (lengths[0] > lengths[lengths.Length - 1] * 0.45d) continue;
                if (lengths[lengths.Length - 1] + lengths[lengths.Length - 2] < MinimumAxisLength * 2d) continue;

                var minimum = chain.Min(item => item.Minimum);
                var maximum = chain.Max(item => item.Maximum);
                var start = new Point2(seed.Start.X + direction.X * minimum, seed.Start.Y + direction.Y * minimum);
                var end = new Point2(seed.Start.X + direction.X * maximum, seed.Start.Y + direction.Y * maximum);
                if (GeometryMath.Distance(start, end) < MinimumAxisLength) continue;

                var members = chain.Select(item => item.Line).ToArray();
                groups.Add(new SegmentedAxisGroup(
                    new AxisCandidate(
                        start,
                        end,
                        0.90d,
                        members.SelectMany(line => line.ProvenanceIds).Distinct().ToArray())
                    {
                        SourceClaims = RecognizerSourceClaimBuilder.FromProvenance(
                            SourceUsageRole.AxisGeometry,
                            members.SelectMany(line => line.ProvenanceIds).ToArray())
                    },
                    members));
                foreach (var member in members)
                    used.Add(member);
                break;
            }
        }

        return groups;
    }

    private static Point2 CanonicalDirection(LinePrimitive line)
    {
        var direction = GeometryMath.Normalize(GeometryMath.Subtract(line.End, line.Start));
        return direction.X < -1e-9 || (Math.Abs(direction.X) <= 1e-9 && direction.Y < 0d)
            ? new Point2(-direction.X, -direction.Y)
            : direction;
    }

    private static ProjectedLine Project(LinePrimitive line, Point2 origin, Point2 direction)
    {
        var first = GeometryMath.Dot(GeometryMath.Subtract(line.Start, origin), direction);
        var second = GeometryMath.Dot(GeometryMath.Subtract(line.End, origin), direction);
        return new ProjectedLine(line, Math.Min(first, second), Math.Max(first, second));
    }

    private sealed record ProjectedLine(LinePrimitive Line, double Minimum, double Maximum);

    private sealed record SegmentedAxisGroup(AxisCandidate Candidate, IReadOnlyList<LinePrimitive> Lines);
}

public sealed record AxisRecognitionResult(
    IReadOnlyList<AxisCandidate> NativeAxes,
    IReadOnlyList<SemanticWarning> Warnings);
