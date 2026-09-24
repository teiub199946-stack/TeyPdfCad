using System.Globalization;
using System.Text.RegularExpressions;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.Core.Recognition;

public sealed class LevelRecognizer
{
    private static readonly Regex LevelText = new(
        @"^(?:ОТМ\.?|УРОВЕНЬ|УРОВ\.?|[±+\-])\s*[-+]?\d+(?:[.,]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const double MarkerTolerance = 12d;

    public LevelRecognitionResult Recognize(PrimitiveScene scene)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        var levels = new List<LevelCandidate>();
        var warnings = new List<SemanticWarning>();
        foreach (var text in scene.Texts.Where(text => LooksLikeLevel(text.Value)))
        {
            var nearby = scene.Lines
                .Where(line => GeometryMath.Distance(line.Start, text.Position) <= MarkerTolerance
                    || GeometryMath.Distance(line.End, text.Position) <= MarkerTolerance)
                .ToArray();
            var marker = nearby.FirstOrDefault(line => GeometryMath.Distance(line.Start, line.End) <= 10d);
            var markerSupport = marker is null
                ? []
                : scene.Lines
                    .Where(line => line != marker && (Touches(line, marker.Start) || Touches(line, marker.End)))
                    .ToArray();
            var hasMarkerGeometry = marker is not null && markerSupport.Length >= 1;

            if (hasMarkerGeometry)
            {
                var markerPoint = marker!.Start;
                var markerProvenance = marker.ProvenanceIds
                    .Concat(markerSupport.SelectMany(line => line.ProvenanceIds))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                levels.Add(new LevelCandidate(
                    markerPoint,
                    text.Position,
                    text.Value,
                    0.92d,
                    markerProvenance.Concat(text.ProvenanceIds).Distinct(StringComparer.Ordinal).ToArray())
                {
                    SourceClaims = RecognizerSourceClaimBuilder.FromProvenance(
                            SourceUsageRole.LevelMarker,
                            markerProvenance)
                        .Concat(RecognizerSourceClaimBuilder.FromProvenance(
                            SourceUsageRole.Text,
                            text.ProvenanceIds))
                        .Distinct()
                        .ToArray()
                });
            }
            else
            {
                warnings.Add(new SemanticWarning(
                    "level-low-confidence",
                    "Level-like text has no sufficiently clear marker geometry.",
                    text.ProvenanceIds));
            }
        }

        return new LevelRecognitionResult(levels, warnings);
    }

    private static bool LooksLikeLevel(string value)
    {
        var normalized = value.Replace(" ", string.Empty)
            .Replace(',', '.');
        if (normalized.StartsWith("±", StringComparison.Ordinal)
            || normalized.StartsWith("+", StringComparison.Ordinal)
            || normalized.StartsWith("-", StringComparison.Ordinal)
            || normalized.StartsWith("ОТМ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("УРОВ", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(
                normalized.TrimStart('±', '+', '-').Replace("ОТМ.", string.Empty)
                    .Replace("ОТМ", string.Empty)
                    .Replace("УРОВЕНЬ", string.Empty)
                    .Replace("УРОВ.", string.Empty)
                    .Replace("УРОВ", string.Empty),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _);

        return false;
    }

    private static bool Touches(LinePrimitive line, Point2 point)
        => GeometryMath.Distance(line.Start, point) <= 0.75d
            || GeometryMath.Distance(line.End, point) <= 0.75d;
}

public sealed record LevelRecognitionResult(
    IReadOnlyList<LevelCandidate> NativeLevels,
    IReadOnlyList<SemanticWarning> Warnings);
