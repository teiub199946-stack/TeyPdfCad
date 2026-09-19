namespace TeyPdfCad.Core.Conversion;

public sealed record ConversionReport(
    int PagesRead,
    int PagesWritten,
    IReadOnlyList<PageConversionReport> Pages);

public sealed record PageConversionReport(
    int PageNumber,
    bool Complete,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
