using TeyPdfCad.Core.Documents;

namespace TeyPdfCad.Core.Conversion;

public sealed class DocumentLayoutPlanner
{
    private const double ModelSpaceGapMillimetres = 100d;

    public DwgDocumentPlan Create(VectorPdfDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var sheets = new List<SheetPlan>(document.Pages.Count);
        var modelOriginX = 0d;

        foreach (var page in document.Pages.OrderBy(page => page.Number))
        {
            var width = page.WidthMillimetres;
            var height = page.HeightMillimetres;

            sheets.Add(new SheetPlan(
                PageNumber: page.Number,
                LayoutName: $"Лист-{page.Number:D3}",
                PaperWidthMillimetres: width,
                PaperHeightMillimetres: height,
                ModelOriginX: modelOriginX,
                ModelOriginY: 0d));

            modelOriginX += width + ModelSpaceGapMillimetres;
        }

        return new DwgDocumentPlan(sheets);
    }
}

public sealed record DwgDocumentPlan(IReadOnlyList<SheetPlan> Sheets);

public sealed record SheetPlan(
    int PageNumber,
    string LayoutName,
    double PaperWidthMillimetres,
    double PaperHeightMillimetres,
    double ModelOriginX,
    double ModelOriginY);
