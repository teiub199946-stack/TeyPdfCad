using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.Core.Templates;

public sealed class TemplateLibrary
{
    public TemplateLibrary(IReadOnlyList<TemplateSheet> sheets, IReadOnlyList<TemplateBlockDefinition>? blocks = null)
    {
        Sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        Blocks = blocks ?? [];
    }

    public IReadOnlyList<TemplateSheet> Sheets { get; }

    public IReadOnlyList<TemplateBlockDefinition> Blocks { get; }
}

public sealed record TemplateBlockDefinition(
    string Name,
    IReadOnlyList<TemplateGeometryEntity> Entities,
    IReadOnlyList<TemplateAttributeDefinition> Attributes);

public sealed record TemplateGeometryEntity(
    string ObjectClass,
    string Handle,
    IReadOnlyList<TemplatePoint> Points,
    string? Text,
    double? TextHeight,
    string? Layer);

public sealed record TemplatePoint(double X, double Y);

public sealed record TemplateAttributeDefinition(string Tag, string Prompt, string DefaultValue);

public sealed record TemplateSheet(
    string Name,
    StandardSheetFormat Format,
    SheetOrientation Orientation);

public sealed record TemplateSelection(
    bool IsConfirmed,
    string? TemplateName,
    string Reason,
    IReadOnlyList<string>? SourceIds = null)
{
    public IReadOnlyList<string> SourceIdsToReplace => SourceIds ?? [];

    public static TemplateSelection Rejected(string reason) => new(false, null, reason);
}
