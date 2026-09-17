using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

public sealed record DiagnosticCaseRecord
{
    public DimensionCase Expected { get; init; } = new();
    public ActualDimensionResult Actual { get; init; } = new();
    public CaseGeometryTrace Trace { get; init; } = new();
    public CaseDiagnostic Diagnostic { get; init; } = new();
}

public sealed record RateMetric
{
    public int Correct { get; init; }
    public int Total { get; init; }
    public double Rate { get; init; }
}

public sealed record ErrorPercentiles
{
    public int Count { get; init; }
    public double P50 { get; init; }
    public double P90 { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }
    public double Max { get; init; }
}

public sealed record CohortStats
{
    public string Name { get; init; } = string.Empty;
    public int Total { get; init; }
    public int RealCoreDefects { get; init; }
    public int NoiseOrNumericExplained { get; init; }
    public int FalsePositive { get; init; }
    public int FalseNegative { get; init; }
    public RateMetric CorrectGeometry { get; init; } = new();
}

public sealed record PointBiasSummary
{
    public int EvaluatedCases { get; init; }
    public double MeanDxPaper { get; init; }
    public double MeanDyPaper { get; init; }
    public double MagnitudePaper { get; init; }
    public bool IsSystematic { get; init; }
}

public sealed record ScaleDiagnosticSummary
{
    public int EvaluatedCases { get; init; }
    public int WrongScaleCases { get; init; }
    public double WrongScaleRate { get; init; }
    public double MeanSignedRelativeError { get; init; }
    public bool HasSystematicScaleError { get; init; }
}

public sealed record LegacyWrongPointsSummary
{
    public int Total { get; init; }
    public int RealCoreDefects { get; init; }
    public int NoiseOrNumericExplained { get; init; }
    public int Other { get; init; }
}

public sealed record CategoryCount
{
    public DiagnosticCategory Category { get; init; }
    public int Count { get; init; }
}

public sealed record DiagnosticRegressionReport
{
    public string SchemaVersion { get; init; } = "3.0";
    public int Seed { get; init; }
    public int Total { get; init; }
    public int TruePositive { get; init; }
    public int TrueNegative { get; init; }
    public int FalsePositive { get; init; }
    public int FalseNegative { get; init; }
    public double DetectionPrecision { get; init; }
    public double DetectionRecall { get; init; }
    public double F1 { get; init; }
    public RateMetric CorrectValue { get; init; } = new();
    public RateMetric CorrectScale { get; init; } = new();
    public RateMetric CorrectType { get; init; } = new();
    public RateMetric CorrectCount { get; init; } = new();
    public RateMetric CorrectGeometry { get; init; } = new();
    public RateMetric AbstentionAccuracy { get; init; } = new();
    public ErrorPercentiles AllGeometryErrorPaper { get; init; } = new();
    public ErrorPercentiles AllGeometryErrorWorld { get; init; } = new();
    public ErrorPercentiles RealDefectGeometryErrorPaper { get; init; } = new();
    public ErrorPercentiles RealDefectGeometryErrorWorld { get; init; } = new();
    public PointBiasSummary PointBias { get; init; } = new();
    public ScaleDiagnosticSummary ScaleDiagnostics { get; init; } = new();
    public LegacyWrongPointsSummary LegacyWrongPoints { get; init; } = new();
    public List<CategoryCount> Categories { get; init; } = new();
    public List<CohortStats> Cohorts { get; init; } = new();
    public List<DiagnosticCaseRecord> Cases { get; init; } = new();
}

public sealed record WorstCaseRecord
{
    public string CaseId { get; init; } = string.Empty;
    public int Seed { get; init; }
    public DiagnosticCategory Category { get; init; }
    public double DrawingScale { get; init; }
    public NoiseSpec InjectedNoise { get; init; } = new();
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public double MaxPaperError { get; init; }
    public double MaxWorldError { get; init; }
    public string Explanation { get; init; } = string.Empty;
}
