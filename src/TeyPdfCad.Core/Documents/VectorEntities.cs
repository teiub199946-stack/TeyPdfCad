using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Documents;

public sealed record VectorStyle(
    string? SourceLayer = null,
    int? RgbColor = null,
    double? StrokeWidthPoints = null,
    IReadOnlyList<double>? DashPatternPoints = null);

public abstract record VectorEntity(
    string SourceId,
    VectorStyle Style,
    double Confidence = 1d);

public sealed record VectorLine(
    string SourceId,
    Point2 Start,
    Point2 End,
    VectorStyle Style,
    double Confidence = 1d)
    : VectorEntity(SourceId, Style, Confidence);

public sealed record VectorPolyline(
    string SourceId,
    IReadOnlyList<Point2> Vertices,
    bool IsClosed,
    VectorStyle Style,
    double Confidence = 1d)
    : VectorEntity(SourceId, Style, Confidence);

public enum VectorFillRule
{
    NonZero,
    EvenOdd
}

public sealed record VectorFilledPath(
    string SourceId,
    IReadOnlyList<Point2> Boundary,
    VectorFillRule FillRule,
    VectorStyle Style,
    double Confidence = 1d,
    IReadOnlyList<IReadOnlyList<Point2>>? InteriorBoundaries = null)
    : VectorEntity(SourceId, Style, Confidence)
{
    public IReadOnlyList<IReadOnlyList<Point2>> InteriorBoundaries { get; init; } = InteriorBoundaries ?? [];

    public IReadOnlyList<IReadOnlyList<Point2>> Loops => [Boundary, ..InteriorBoundaries];
}

public sealed record VectorText(
    string SourceId,
    string Value,
    Point2 InsertionPoint,
    double HeightPoints,
    VectorStyle Style,
    double Confidence = 1d,
    double RotationRadians = 0d,
    double AdvanceWidthPoints = 0d,
    string? FontName = null,
    Point2? VisualCenter = null)
    : VectorEntity(SourceId, Style, Confidence);
