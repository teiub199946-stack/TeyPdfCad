using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Semantics;

public sealed record SemanticReconstructionResult(
    IReadOnlyList<DimensionCandidate> Dimensions,
    IReadOnlyList<DimensionChain> DimensionChains,
    double? DominantDrawingScale,
    double AverageDimensionConfidence)
{
    public int ReconstructedObjectCount => Dimensions.Count;

    public IReadOnlyList<double> DetectedDrawingScales => Dimensions
        .Select(x => Math.Round(x.DrawingScale, 6))
        .Distinct()
        .OrderBy(x => x)
        .ToArray();
}
