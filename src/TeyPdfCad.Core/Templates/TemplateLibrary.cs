using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.Core.Templates;

public sealed class TemplateLibrary
{
    public TemplateLibrary(IReadOnlyList<TemplateSheet> sheets)
    {
        Sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
    }

    public IReadOnlyList<TemplateSheet> Sheets { get; }
}

public sealed record TemplateSheet(
    string Name,
    StandardSheetFormat Format,
    SheetOrientation Orientation);

public sealed record TemplateSelection(bool IsConfirmed, string? TemplateName, string Reason)
{
    public static TemplateSelection Rejected(string reason) => new(false, null, reason);
}
