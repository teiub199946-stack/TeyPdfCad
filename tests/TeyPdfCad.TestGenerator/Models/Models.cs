namespace TeyPdfCad.TestGenerator.Models;

public enum DimensionType
{
    Linear,
    Rotated,
    Aligned,
    Chain,
    Negative
}

public enum ExpectedResult
{
    Recognized,
    Rejected,
    Ambiguous
}

public enum ConfidenceClass
{
    None,
    Low,
    Medium,
    High
}

public enum TextPlacement
{
    Centered,
    Above,
    Below,
    OffsetLeft,
    OffsetRight,
    OutsideLeft,
    OutsideRight
}

public enum ArrowType
{
    ClosedFilled,
    ClosedBlank,
    Open,
    ArchitecturalTick,
    Oblique,
    Dot
}

public enum NegativePattern
{
    None,
    LinesTextNoArrows,
    ArrowsLineNoExtensionLines,
    TextNearOrdinaryLine,
    ArrowsNearWall,
    TableLineNumber,
    AxisText,
    RandomLinesNumber,
    NumberInsideBlock,
    NumberNearPolyline
}

public enum ComparisonOutcome
{
    Correct,
    FalsePositive,
    FalseNegative,
    WrongValue,
    WrongPoints,
    WrongType,
    WrongScale,
    WrongConfidence,
    WrongCount,
    AmbiguousMismatch,
    MissingActual
}

public readonly record struct Point2D(double X, double Y)
{
    public double DistanceTo(Point2D other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

public sealed record NoiseSpec
{
    public double CoordinateJitter { get; init; }
    public bool MicroBreak { get; init; }
    public bool EndpointMismatch { get; init; }
    public double AngularSkewDegrees { get; init; }
    public Point2D TextOffset { get; init; }
}

public sealed record ObservedGeometry
{
    public Point2D P1 { get; init; }
    public Point2D P2 { get; init; }
    public Point2D DimensionLinePoint { get; init; }
    public Point2D TextPosition { get; init; }
}

public sealed record DimensionSegment
{
    public double ExpectedValue { get; init; }
    public Point2D P1 { get; init; }
    public Point2D P2 { get; init; }
}

public sealed record DimensionCase
{
    public string Id { get; init; } = string.Empty;
    public int Seed { get; init; }
    public DimensionType DimensionType { get; init; }
    public double ExpectedValue { get; init; }
    public Point2D P1 { get; init; }
    public Point2D P2 { get; init; }
    public Point2D DimensionLinePoint { get; init; }
    public double Rotation { get; init; }
    public Point2D TextPosition { get; init; }
    public TextPlacement TextPlacement { get; init; }
    public double TextHeight { get; init; }
    public ArrowType ArrowType { get; init; }
    public double ArrowSize { get; init; }
    public double ExtensionLineOffset { get; init; }
    public double ExtensionLineExtension { get; init; }
    public double DrawingScale { get; init; }
    public ConfidenceClass ExpectedConfidenceClass { get; init; }
    public ExpectedResult ExpectedResult { get; init; }
    public int ExpectedDimensions { get; init; } = 1;
    public NegativePattern NegativePattern { get; init; }
    public NoiseSpec Noise { get; init; } = new();
    public ObservedGeometry ObservedGeometry { get; init; } = new();
    public bool IsTextFlipped { get; init; }
    public bool IsDimensionLineBroken { get; init; }
    public bool IsInsideGeometry { get; init; }
    public List<DimensionSegment> Segments { get; init; } = new();
    public List<string> Tags { get; init; } = new();
}

public sealed record TestCorpus
{
    public string SchemaVersion { get; init; } = "1.0";
    public int Seed { get; init; }
    public List<DimensionCase> Cases { get; init; } = new();
}

public sealed record ActualDimensionResult
{
    public string CaseId { get; init; } = string.Empty;
    public ExpectedResult Result { get; init; }
    public int DetectedDimensions { get; init; }
    public double? Value { get; init; }
    public Point2D? P1 { get; init; }
    public Point2D? P2 { get; init; }
    public Point2D? DimensionLinePoint { get; init; }
    public DimensionType? DimensionType { get; init; }
    public double? DrawingScale { get; init; }
    public ConfidenceClass? ConfidenceClass { get; init; }
    public bool IsMissing { get; init; }
    public List<string> Diagnostics { get; init; } = new();
}

public sealed record CaseComparison
{
    public string CaseId { get; init; } = string.Empty;
    public ComparisonOutcome Outcome { get; init; }
    public bool Passed { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public List<string> Reasons { get; init; } = new();
}

public sealed record PerformanceMetrics
{
    public double TotalRuntimeSeconds { get; init; }
    public double CasesPerSecond { get; init; }
    public double MedianCaseMilliseconds { get; init; }
    public double P95CaseMilliseconds { get; init; }
    public double P99CaseMilliseconds { get; init; }
}

public sealed record BreakdownRow
{
    public string Category { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int FalsePositive { get; init; }
    public int FalseNegative { get; init; }
    public int WrongValue { get; init; }
    public int WrongGeometry { get; init; }
    public double PassRate { get; init; }
}

public sealed record MetricDelta
{
    public string Metric { get; init; } = string.Empty;
    public double Baseline { get; init; }
    public double Current { get; init; }
    public double Delta { get; init; }
    public bool IsRegression { get; init; }
}

public sealed record BaselineComparison
{
    public bool HasBaseline { get; init; }
    public bool IsRegression { get; init; }
    public List<MetricDelta> Deltas { get; init; } = new();
}

public sealed record ReleaseGateResult
{
    public string Status { get; init; } = "PASS";
    public List<string> Reasons { get; init; } = new();
}

public sealed record RegressionReport
{
    public int Seed { get; init; }
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int ExpectedDimensions { get; init; }
    public int ExpectedNegatives { get; init; }
    public int Correct { get; init; }
    public int TruePositive { get; init; }
    public int TrueNegative { get; init; }
    public int FalsePositive { get; init; }
    public int FalseNegative { get; init; }
    public int WrongGeometry { get; init; }
    public int WrongValue { get; init; }
    public int WrongType { get; init; }
    public int WrongScale { get; init; }
    public int WrongConfidence { get; init; }
    public int WrongCount { get; init; }
    public int MissingActual { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1 { get; init; }
    public double PassRate { get; init; }
    public double FalsePositiveRate { get; init; }
    public double WrongMeasurementRate { get; init; }
    public PerformanceMetrics Performance { get; init; } = new();
    public List<BreakdownRow> Breakdowns { get; init; } = new();
    public List<CaseComparison> Failures { get; init; } = new();
    public BaselineComparison BaselineComparison { get; init; } = new();
    public ReleaseGateResult ReleaseGate { get; init; } = new();
}

public sealed record RegressionBaseline
{
    public string SchemaVersion { get; init; } = "1.0";
    public int Seed { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1 { get; init; }
    public double PassRate { get; init; }
    public double FalsePositiveRate { get; init; }
    public double WrongMeasurementRate { get; init; }
}

public sealed record TestConfig
{
    public int DefaultCount { get; init; } = 10000;
    public int DefaultSeed { get; init; } = 12345;
    public double ValueToleranceAbsolute { get; init; } = 0.01;
    public double PointTolerance { get; init; } = 0.05;
    public double ScaleTolerance { get; init; } = 0.000001;
    public double MaxFalsePositiveRate { get; init; } = 0.005;
    public double MaxWrongMeasurementRate { get; init; } = 0.002;
    public double MaxRecallDrop { get; init; } = 0.001;
    public double MaxPrecisionDrop { get; init; } = 0.001;
    public double MaxPassRateDrop { get; init; } = 0.001;
    public bool FailOnBaselineRegression { get; init; } = true;
}
