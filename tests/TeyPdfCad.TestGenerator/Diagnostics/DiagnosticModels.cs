using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

public enum DiagnosticCategory
{
    Correct,
    ExpectedNoisePropagation,
    NumericTolerance,
    WrongGeometry,
    WrongValue,
    WrongScale,
    WrongDimensionType,
    WrongDimensionCount,
    UnexpectedDetection,
    MissedDetection,
    ExpectedAbstention,
    WrongAbstention,
    ChainMismatch
}

public enum GeometryCheckStatus
{
    Correct,
    ExpectedNoisePropagation,
    NumericTolerance,
    Wrong,
    Unavailable
}

public sealed record PointDiagnosticInput
{
    public Point2D ExpectedWorld { get; init; }
    public Point2D ActualWorld { get; init; }
    public Point2D CleanPaper { get; init; }
    public Point2D CoreInputPaper { get; init; }
    public Point2D ActualCorePaper { get; init; }
    public double DrawingScale { get; init; }
}

public sealed record GeometryToleranceResult
{
    public DiagnosticCategory Category { get; init; }
    public double InjectedPaperError { get; init; }
    public double AllowedPaperError { get; init; }
    public double AllowedWorldError { get; init; }
    public double ActualPaperError { get; init; }
    public double ActualWorldError { get; init; }
}

public sealed record GeometryPointTrace
{
    public Point2D ExpectedWorld { get; init; }
    public Point2D CleanPaper { get; init; }
    public Point2D? CoreInputPaper { get; init; }
    public Point2D InjectedPaperDelta { get; init; }
}

public sealed record CoreGeometrySnapshot
{
    public Point2D? DefinitionPoint1Paper { get; init; }
    public Point2D? DefinitionPoint2Paper { get; init; }
    public Point2D? DimensionLinePointPaper { get; init; }
    public Point2D? TextAnchorPaper { get; init; }
    public Point2D? DefinitionPoint1World { get; init; }
    public Point2D? DefinitionPoint2World { get; init; }
    public Point2D? DimensionLinePointWorld { get; init; }
    public Point2D? TextAnchorWorld { get; init; }
    public double? TextRotationDegrees { get; init; }
    public bool? BrokenDimensionLine { get; init; }
    public List<string> ProvenanceIds { get; init; } = new();
}

public sealed record CaseGeometryTrace
{
    public string CaseId { get; init; } = string.Empty;
    public double DrawingScale { get; init; }
    public NoiseSpec InjectedNoise { get; init; } = new();
    public GeometryPointTrace DefinitionPoint1 { get; init; } = new();
    public GeometryPointTrace DefinitionPoint2 { get; init; } = new();
    public GeometryPointTrace DimensionLineLocation { get; init; } = new();
    public GeometryPointTrace TextAnchor { get; init; } = new();
    public double ExpectedRotationDegrees { get; init; }
    public double? CoreInputTextRotationDegrees { get; init; }
    public bool ExpectedBrokenDimensionLine { get; init; }
    public bool CoreInputBrokenDimensionLine { get; init; }
    public List<string> SceneProvenanceIds { get; init; } = new();
    public CoreGeometrySnapshot? CoreResult { get; init; }
}

public sealed record SemanticCoreDiagnosticRun
{
    public ActualDimensionResult Actual { get; init; } = new();
    public CaseGeometryTrace Trace { get; init; } = new();
}

public sealed record GeometrySubcheck
{
    public string Component { get; init; } = string.Empty;
    public GeometryCheckStatus Status { get; init; }
    public GeometryToleranceResult? Error { get; init; }
    public string Explanation { get; init; } = string.Empty;
}
