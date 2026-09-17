namespace TeyPdfCad.Core.Semantics.Dimensions;

public enum DimensionTextKind
{
    Linear,
    Diameter,
    Radius,
    Unknown
}

public sealed record ParsedDimensionText(
    DimensionTextKind Kind,
    double NominalValue,
    string RawText);
