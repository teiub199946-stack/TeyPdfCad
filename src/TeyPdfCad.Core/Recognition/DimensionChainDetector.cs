using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public sealed class DimensionChainDetector
{
    public IReadOnlyList<DimensionChain> Detect(IReadOnlyList<DimensionCandidate> dimensions)
    {
        if (dimensions.Count < 2) return [];

        var visited = new bool[dimensions.Count];
        var chains = new List<DimensionChain>();

        for (var start = 0; start < dimensions.Count; start++)
        {
            if (visited[start]) continue;

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                for (var candidate = 0; candidate < dimensions.Count; candidate++)
                {
                    if (visited[candidate]) continue;
                    if (!AreAdjacent(dimensions[current], dimensions[candidate])) continue;
                    visited[candidate] = true;
                    queue.Enqueue(candidate);
                }
            }

            if (component.Count < 2) continue;
            var members = component.Select(index => dimensions[index]).ToList();
            SortAlongChain(members);
            chains.Add(new DimensionChain(
                members,
                members.Average(x => x.DrawingScale),
                members.Average(x => x.Confidence)));
        }

        return chains;
    }

    private static bool AreAdjacent(DimensionCandidate first, DimensionCandidate second)
    {
        var scaleError = Math.Abs(first.DrawingScale - second.DrawingScale)
            / Math.Max(Math.Max(first.DrawingScale, second.DrawingScale), 1e-9);
        if (scaleError > 0.01) return false;

        var firstVector = GeometryMath.Subtract(first.DefinitionPoint2, first.DefinitionPoint1);
        var secondVector = GeometryMath.Subtract(second.DefinitionPoint2, second.DefinitionPoint1);
        var firstLength = GeometryMath.Length(firstVector);
        var secondLength = GeometryMath.Length(secondVector);
        if (firstLength <= 1e-9 || secondLength <= 1e-9) return false;

        var firstUnit = GeometryMath.Normalize(firstVector);
        var secondUnit = GeometryMath.Normalize(secondVector);
        if (Math.Abs(GeometryMath.Dot(firstUnit, secondUnit)) < Math.Cos(3.0 * Math.PI / 180.0)) return false;

        var lineOffsetTolerance = Math.Max(Math.Min(firstLength, secondLength) * 0.03, 1e-6);
        if (GeometryMath.DistancePointToInfiniteLine(
                second.DimensionLinePoint,
                first.DimensionLinePoint,
                new Point2(first.DimensionLinePoint.X + firstUnit.X, first.DimensionLinePoint.Y + firstUnit.Y))
            > lineOffsetTolerance)
            return false;

        var endpointTolerance = Math.Max(Math.Min(firstLength, secondLength) * 0.03, 1e-6);
        return EndpointDistance(first, second) <= endpointTolerance;
    }

    private static double EndpointDistance(DimensionCandidate first, DimensionCandidate second)
    {
        return new[]
        {
            GeometryMath.Distance(first.DefinitionPoint1, second.DefinitionPoint1),
            GeometryMath.Distance(first.DefinitionPoint1, second.DefinitionPoint2),
            GeometryMath.Distance(first.DefinitionPoint2, second.DefinitionPoint1),
            GeometryMath.Distance(first.DefinitionPoint2, second.DefinitionPoint2)
        }.Min();
    }

    private static void SortAlongChain(List<DimensionCandidate> dimensions)
    {
        var first = dimensions[0];
        var axis = GeometryMath.Normalize(GeometryMath.Subtract(first.DefinitionPoint2, first.DefinitionPoint1));
        dimensions.Sort((a, b) =>
        {
            var aProjection = GeometryMath.Dot(a.DimensionLinePoint, axis);
            var bProjection = GeometryMath.Dot(b.DimensionLinePoint, axis);
            return aProjection.CompareTo(bProjection);
        });
    }
}
