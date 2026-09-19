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
        ArgumentNullException.ThrowIfNull(scene);

        var axes = new List<AxisCandidate>();
        var warnings = new List<SemanticWarning>();
        foreach (var line in scene.Lines.Where(line => GeometryMath.Distance(line.Start, line.End) >= MinimumAxisLength))
        {
            var layerEvidence = line.Layer?.Contains("ОС", StringComparison.OrdinalIgnoreCase) == true ? 0.55d : 0d;
            var dashEvidence = line.StrokeDashPattern.Count >= 3 ? 0.45d : 0d;
            var confidence = layerEvidence + dashEvidence;
            if (confidence >= NativeAxisThreshold)
            {
                axes.Add(new AxisCandidate(line.Start, line.End, confidence, line.ProvenanceIds));
            }
            else
            {
                warnings.Add(new SemanticWarning(
                    "axis-low-confidence",
                    "Line remains editable geometry because it lacks both axis layer and dash-dot evidence.",
                    line.ProvenanceIds));
            }
        }

        return new AxisRecognitionResult(axes, warnings);
    }
}

public sealed record AxisRecognitionResult(
    IReadOnlyList<AxisCandidate> NativeAxes,
    IReadOnlyList<SemanticWarning> Warnings);
