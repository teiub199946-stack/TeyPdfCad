using TeyPdfCad.Core.Semantics.Dimensions;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Semantics;

public sealed record SemanticReconstructionResult(
    IReadOnlyList<DimensionCandidate> Dimensions,
    IReadOnlyList<DimensionChain> DimensionChains,
    double? DominantDrawingScale,
    double AverageDimensionConfidence)
{
    public IReadOnlyList<AxisCandidate> Axes { get; init; } = [];

    public IReadOnlyList<LeaderCandidate> Leaders { get; init; } = [];

    public IReadOnlyList<LevelCandidate> Levels { get; init; } = [];

    public IReadOnlyList<ArcDimensionCandidate> ArcDimensions { get; init; } = [];

    public IReadOnlyList<ClosedPathPrimitive> NativeFillPaths { get; init; } = [];

    public IReadOnlyList<SemanticWarning> Warnings { get; init; } = [];

    public int ReconstructedObjectCount => Dimensions.Count + Axes.Count + Leaders.Count + Levels.Count + ArcDimensions.Count + NativeFillPaths.Count;

    public IReadOnlyList<double> DetectedDrawingScales => Dimensions
        .Select(x => Math.Round(x.DrawingScale, 6))
        .Distinct()
        .OrderBy(x => x)
        .ToArray();
}
