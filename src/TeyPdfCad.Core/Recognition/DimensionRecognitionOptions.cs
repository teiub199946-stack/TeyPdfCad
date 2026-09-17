namespace TeyPdfCad.Core.Recognition;

public sealed record DimensionRecognitionOptions
{
    public double? DrawingScale { get; init; }
    public double MinConfidence { get; init; } = 0.75;
    public double MeasurementRelativeTolerance { get; init; } = 0.02;
    public double CanonicalScaleRelativeTolerance { get; init; } = 0.03;
    public double ScaleConsensusRelativeTolerance { get; init; } = 0.02;
    public int ScaleConsensusMinimumVotes { get; init; } = 2;
    public double PerpendicularAngleToleranceDegrees { get; init; } = 15.0;
    public double TextDistanceHeightMultiplier { get; init; } = 4.0;
    public double EndpointToleranceHeightMultiplier { get; init; } = 0.75;
}
