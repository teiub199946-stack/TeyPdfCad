using System.Globalization;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed class ArcDimensionRecognizer
{
    private const double TextDistanceTolerance = 30d;

    public ArcDimensionRecognitionResult Recognize(PrimitiveScene scene)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        var candidates = new List<ArcDimensionCandidate>();
        var warnings = new List<SemanticWarning>();
        foreach (var arc in scene.Arcs)
        {
            var text = scene.Texts
                .Where(text => IsArcLengthLabel(text.Value))
                .OrderBy(text => GeometryMath.Distance(text.Position, PointAt(arc)))
                .FirstOrDefault();
            if (text is null || GeometryMath.Distance(text.Position, PointAt(arc)) > TextDistanceTolerance)
                continue;

            candidates.Add(new ArcDimensionCandidate(
                arc.Center,
                arc.Radius,
                arc.StartAngleRadians,
                arc.EndAngleRadians,
                text.Position,
                text.Value,
                0.94d,
                arc.ProvenanceIds.Concat(text.ProvenanceIds).Distinct().ToArray()));
        }

        return new ArcDimensionRecognitionResult(candidates, warnings);
    }

    private static bool IsArcLengthLabel(string value)
    {
        var normalized = value.Trim().Replace(',', '.');
        if (normalized.StartsWith("R", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Ø", StringComparison.Ordinal))
            return false;

        return normalized.StartsWith("L=", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ДЛ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ДУГ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("⌒", StringComparison.Ordinal)
            || (normalized.Length > 1
                && double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                && normalized.Contains("↕"));
    }

    private static Point2 PointAt(ArcPrimitive arc)
        => new(
            arc.Center.X + arc.Radius * Math.Cos((arc.StartAngleRadians + arc.EndAngleRadians) / 2d),
            arc.Center.Y + arc.Radius * Math.Sin((arc.StartAngleRadians + arc.EndAngleRadians) / 2d));
}

public sealed record ArcDimensionRecognitionResult(
    IReadOnlyList<ArcDimensionCandidate> NativeArcDimensions,
    IReadOnlyList<SemanticWarning> Warnings);
