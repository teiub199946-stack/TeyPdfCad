using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.Core.Templates;

public sealed class TemplateSheetSelector
{
    private readonly TemplateLibrary _library;

    public TemplateSheetSelector(TemplateLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public TemplateSelection Select(SheetMetadata sheet, TitleBlockMetadata? titleBlock)
    {
        if (sheet is null) throw new ArgumentNullException(nameof(sheet));
        if (titleBlock is not { IsCandidate: true })
            return TemplateSelection.Rejected("title-block-not-confirmed");

        var template = _library.Sheets.SingleOrDefault(candidate =>
            candidate.Format == sheet.Format && candidate.Orientation == sheet.Orientation);
        return template is null
            ? TemplateSelection.Rejected("template-not-found")
            : new TemplateSelection(true, template.Name, "format-and-title-block-confirmed");
    }
}
