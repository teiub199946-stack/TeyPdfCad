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
