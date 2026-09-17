namespace TeyPdfCad.AutoCAD;

public sealed class ArcTessellationStep
{
    public ArcTessellationStep(double startParameter, double endParameter, string sourceId)
    {
        StartParameter = startParameter;
        EndParameter = endParameter;
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
    }

    public double StartParameter { get; }
    public double EndParameter { get; }
    public string SourceId { get; }
}

/// <summary>
/// Produces deterministic parameter intervals for approximating an AutoCAD circular arc
/// with line chords. A ten-degree maximum step keeps vector glyph curves dense enough
/// for downstream text-shape recognition while preserving a bounded primitive count.
/// </summary>
public static class ArcTessellator
{
    public static readonly double MaxStepRadians = Math.PI / 18.0;

    public static IReadOnlyList<ArcTessellationStep> BuildSteps(
        double startParameter,
        double endParameter,
        string sourceId)
    {
        if (!IsFinite(startParameter))
            throw new ArgumentOutOfRangeException(nameof(startParameter));
        if (!IsFinite(endParameter))
            throw new ArgumentOutOfRangeException(nameof(endParameter));
        if (endParameter <= startParameter)
            throw new ArgumentOutOfRangeException(nameof(endParameter), "Arc end parameter must be greater than start parameter.");
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Source id is required.", nameof(sourceId));

        var sweep = endParameter - startParameter;
        var segmentCount = Math.Max(1, (int)Math.Ceiling(sweep / MaxStepRadians));
        var step = sweep / segmentCount;
        var result = new List<ArcTessellationStep>(segmentCount);

        for (var i = 0; i < segmentCount; i++)
        {
            var from = startParameter + step * i;
            var to = i == segmentCount - 1
                ? endParameter
                : startParameter + step * (i + 1);
            result.Add(new ArcTessellationStep(from, to, $"{sourceId}#arc:{i}"));
        }

        return result;
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
