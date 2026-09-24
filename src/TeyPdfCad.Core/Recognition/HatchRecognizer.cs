using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed class HatchRecognizer
{
    private const double ParallelToleranceRadians = 2d * Math.PI / 180d;
    private const double MaximumSpacingCoefficientOfVariation = 0.15d;
    private const int MaximumPatternLineCandidates = 2_000;
    private const int MaximumPatternBoundaryCandidates = 300;

    public HatchRecognitionResult Recognize(IReadOnlyList<VectorEntity> entities, int pageNumber = 0)
    {
        if (entities is null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        var hatches = new List<HatchCandidate>();
        var warnings = new List<SemanticWarning>();
        var claims = new List<HatchClaim>();
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

        var lineEntities = entities.OfType<VectorLine>().ToArray();
        var boundaries = entities.OfType<VectorPolyline>().Where(path => path.IsClosed && HasValidBoundary(path.Vertices)).ToArray();
        var patternRecognitionSkipped = lineEntities.Length > MaximumPatternLineCandidates
            || boundaries.Length > MaximumPatternBoundaryCandidates;
        if (patternRecognitionSkipped)
        {
            warnings.Add(new SemanticWarning(
                "hatch-recognition-skipped-complexity",
                $"Pattern hatch recognition was skipped safely for {lineEntities.Length} lines and {boundaries.Length} closed boundaries.",
                []));
        }
        else
        {
            var consumedPatternLineIds = new HashSet<string>(StringComparer.Ordinal);
            var texts = entities.OfType<VectorText>().ToArray();
            foreach (var boundary in boundaries.OrderBy(boundary => Math.Abs(SignedArea(boundary.Vertices))))
            {
                var interiorLines = lineEntities
                    .Where(line => !consumedPatternLineIds.Contains(line.SourceId))
                    .Where(line => IsEntirelyInside(boundary.Vertices, line))
                    .ToArray();
                var pattern = TryCreatePatternCandidate(boundary, interiorLines, texts);
                if (pattern is not null)
                {
                    var candidateId = SourceReplacementPlanner.GetCandidateKey(pattern, pageNumber);
                    var classification = HasStrongPatternEvidence(boundary)
                        ? HatchClassification.Confident
                        : HatchClassification.Uncertain;
                    claims.Add(new HatchClaim(
                        candidateId,
                        [boundary.SourceId],
                        pattern.ProvenanceIds.Skip(1).Distinct(StringComparer.Ordinal).ToArray(),
                        classification));

                    if (classification == HatchClassification.Confident)
                    {
                        hatches.Add(pattern);
                        foreach (var sourceId in pattern.ProvenanceIds.Skip(1))
                        {
                            consumedPatternLineIds.Add(sourceId);
                        }
                    }
                    else
                    {
                        warnings.Add(new SemanticWarning(
                            "hatch-uncertain",
                            "Parallel geometry looks hatch-like but lacks strong source evidence; native HATCH is deferred and all source geometry is preserved.",
                            pattern.ProvenanceIds));
                    }
                }
            }
        }

        var unresolvedPatternGroup = patternRecognitionSkipped
            ? Array.Empty<VectorLine>()
            : FindPlausibleParallelGroup(lineEntities);
        if (unresolvedPatternGroup.Length > 0
            && !hatches.Any(candidate => !candidate.IsSolid))
        {
            warnings.Add(new SemanticWarning(
                "hatch-low-confidence",
                "Parallel source lines remain editable geometry because no closed filled boundary proves a hatch.",
                unresolvedPatternGroup.Select(line => line.SourceId).ToArray()));
        }

        return new HatchRecognitionResult(hatches, warnings, claims);
    }

    private static bool HasStrongPatternEvidence(VectorPolyline boundary)
    {
        var layer = boundary.Style.SourceLayer;
        return !string.IsNullOrWhiteSpace(layer)
            && (layer.IndexOf("HATCH", StringComparison.OrdinalIgnoreCase) >= 0
                || layer.IndexOf("ШТРИХ", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool HasValidBoundary(IReadOnlyList<Point2> boundary)
        => boundary.Count >= 3 && boundary.Distinct().Count() >= 3;

    private static double SignedArea(IReadOnlyList<Point2> boundary)
    {
        var twiceArea = 0d;
        for (var index = 0; index < boundary.Count; index++)
        {
            var current = boundary[index];
            var next = boundary[(index + 1) % boundary.Count];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }
        return twiceArea / 2d;
    }

    private static HatchCandidate? TryCreatePatternCandidate(
        VectorPolyline boundary,
        IReadOnlyList<VectorLine> lines,
        IReadOnlyList<VectorText> texts)
    {
        if (lines.Count < 3 || texts.Any(text => Contains(boundary.Vertices, text.InsertionPoint)))
        {
            return null;
        }

        var unit = GeometryMath.Normalize(GeometryMath.Subtract(lines[0].End, lines[0].Start));
        if (GeometryMath.Length(unit) <= 1e-12)
        {
            return null;
        }

        var parallelLines = lines.Where(line => IsParallel(line, unit)).ToArray();
        if (parallelLines.Length < 3)
        {
            return null;
        }

        var normal = new Point2(-unit.Y, unit.X);
        var positions = parallelLines
            .Select(line => GeometryMath.Dot(GeometryMath.Midpoint(line.Start, line.End), normal))
            .OrderBy(position => position)
            .ToArray();
        var spacings = positions.Zip(positions.Skip(1), (first, second) => second - first)
            .Where(spacing => spacing > 1e-9)
            .ToArray();
        if (spacings.Length < 2)
        {
            return null;
        }

        var meanSpacing = spacings.Average();
        var standardDeviation = Math.Sqrt(spacings.Average(spacing => Math.Pow(spacing - meanSpacing, 2)));
        if (standardDeviation / meanSpacing > MaximumSpacingCoefficientOfVariation)
        {
            return null;
        }

        return new HatchCandidate(
            boundary.Vertices,
            IsSolid: false,
            PatternAngleRadians: Math.Atan2(unit.Y, unit.X),
            PatternSpacingMillimetres: meanSpacing,
            boundary.Style,
            Confidence: 0.9d,
            [boundary.SourceId, ..parallelLines.Select(line => line.SourceId)]);
    }

    private static bool IsParallel(VectorLine line, Point2 referenceUnit)
    {
        var unit = GeometryMath.Normalize(GeometryMath.Subtract(line.End, line.Start));
        return Math.Abs(GeometryMath.Dot(unit, referenceUnit)) >= Math.Cos(ParallelToleranceRadians);
    }

    private static bool Contains(IReadOnlyList<Point2> polygon, Point2 point)
    {
        if (IsOnBoundary(polygon, point))
        {
            return true;
        }
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var a = polygon[current];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool IsEntirelyInside(IReadOnlyList<Point2> polygon, VectorLine line)
        => Contains(polygon, line.Start)
           && Contains(polygon, line.End)
           && !IntersectsPolygonBoundary(polygon, line);

    private static bool IntersectsPolygonBoundary(IReadOnlyList<Point2> polygon, VectorLine line)
    {
        for (var current = 0; current < polygon.Count; current++)
        {
            var edgeStart = polygon[current];
            var edgeEnd = polygon[(current + 1) % polygon.Count];
            if (SegmentsIntersect(line.Start, line.End, edgeStart, edgeEnd))
            {
                return true;
            }
        }
        return false;
    }

    private static VectorLine[] FindPlausibleParallelGroup(IReadOnlyList<VectorLine> lines)
    {
        foreach (var line in lines)
        {
            var unit = GeometryMath.Normalize(GeometryMath.Subtract(line.End, line.Start));
            if (GeometryMath.Length(unit) <= 1e-12)
                continue;

            var group = lines.Where(candidate => IsParallel(candidate, unit)).ToArray();
            if (group.Length < 3)
                continue;

            var normal = new Point2(-unit.Y, unit.X);
            var distinctPositions = group
                .Select(candidate => GeometryMath.Dot(
                    GeometryMath.Midpoint(candidate.Start, candidate.End),
                    normal))
                .OrderBy(position => position)
                .Aggregate(
                    new List<double>(),
                    (positions, position) =>
                    {
                        if (positions.Count == 0
                            || Math.Abs(position - positions[^1]) > 1e-6)
                        {
                            positions.Add(position);
                        }
                        return positions;
                    });

            if (distinctPositions.Count >= 3)
                return group;
        }
        return [];
    }

    private static bool IsOnBoundary(IReadOnlyList<Point2> polygon, Point2 point)
        => Enumerable.Range(0, polygon.Count).Any(index => DistanceToSegment(point, polygon[index], polygon[(index + 1) % polygon.Count]) <= 1e-9);

    private static double DistanceToSegment(Point2 point, Point2 start, Point2 end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var squared = dx * dx + dy * dy;
        if (squared <= 1e-18) return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        var t = Math.Max(0d, Math.Min(1d, ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / squared));
        return Math.Sqrt(Math.Pow(point.X - (start.X + t * dx), 2) + Math.Pow(point.Y - (start.Y + t * dy), 2));
    }

    private static bool SegmentsIntersect(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        static double Cross(Point2 first, Point2 second, Point2 third) => (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);
        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);
        return Math.Sign(abC) != Math.Sign(abD) && Math.Sign(cdA) != Math.Sign(cdB);
    }
}

public sealed record HatchRecognitionResult(
    IReadOnlyList<HatchCandidate> NativeHatches,
    IReadOnlyList<SemanticWarning> Warnings,
    IReadOnlyList<HatchClaim>? SourceClaims = null)
{
    public IReadOnlyList<HatchClaim> Claims => SourceClaims ?? [];
}
