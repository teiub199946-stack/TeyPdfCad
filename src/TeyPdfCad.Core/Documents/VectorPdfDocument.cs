namespace TeyPdfCad.Core.Documents;

public sealed record VectorPdfDocument(IReadOnlyList<VectorPdfPage> Pages)
{
    public int PageCount => Pages.Count;
}

public sealed record VectorPdfPage(
    int Number,
    double WidthPoints,
    double HeightPoints,
    int RotationDegrees,
    IReadOnlyList<VectorEntity> Entities)
{
    public const double MillimetresPerPoint = 25.4d / 72d;

    public double WidthMillimetres => WidthPoints * MillimetresPerPoint;

    public double HeightMillimetres => HeightPoints * MillimetresPerPoint;
}
