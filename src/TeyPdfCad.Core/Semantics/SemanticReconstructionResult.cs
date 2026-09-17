using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Semantics;

public sealed record SemanticReconstructionResult(
    IReadOnlyList<DimensionCandidate> Dimensions,
    IReadOnlyList<DimensionChain> DimensionChains,
    double? DominantDrawingScale,
    double AverageDimensionConfidence)
{
    public int ReconstructedObjectCount => Dimensions.Count;
}
