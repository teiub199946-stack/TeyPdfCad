namespace TeyPdfCad.Core.Semantics.Dimensions;

public sealed record DimensionChain(
    IReadOnlyList<DimensionCandidate> Dimensions,
    double DrawingScale,
    double Confidence)
{
    public double TotalDisplayedValue => Dimensions.Sum(x => x.DisplayedValue);
}
