using Newtonsoft.Json;
using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.Core.Templates;

public sealed class TemplateLibraryManifestReader
{
    public TemplateLibrary Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Template manifest is required.", nameof(json));
        var manifest = JsonConvert.DeserializeObject<ManifestDto>(json)
            ?? throw new InvalidDataException("Template manifest is empty.");
        if (manifest.SchemaVersion != "1")
            throw new InvalidDataException($"Unsupported template manifest schema '{manifest.SchemaVersion ?? "missing"}'.");

        var blocks = (manifest.Blocks ?? [])
            .Where(block => !string.IsNullOrWhiteSpace(block.Name))
            .Select(block => new TemplateBlockDefinition(
                block.Name!,
                (block.Entities ?? []).Select(entity => new TemplateGeometryEntity(
                    entity.ObjectClass ?? string.Empty,
                    entity.Handle ?? string.Empty,
                    (entity.Points ?? []).Select(point => new TemplatePoint(point.X, point.Y)).ToArray(),
                    entity.Text,
                    entity.TextHeight,
                    entity.Layer,
                    entity.IsClosed ?? false,
                    entity.RotationRadians ?? 0d,
                    entity.ArcRadius,
                    entity.StartAngleRadians,
                    entity.EndAngleRadians)).ToArray(),
                (block.Attributes ?? []).Select(attribute => new TemplateAttributeDefinition(
                    attribute.Tag ?? string.Empty,
                    attribute.Prompt ?? string.Empty,
                    attribute.DefaultValue ?? string.Empty)).ToArray(),
                block.Origin is null ? null : new TemplatePoint(block.Origin.X, block.Origin.Y)))
            .ToArray();
        var sheets = blocks
            .Select(TryInferSheet)
            .Where(sheet => sheet is not null)
            .Cast<TemplateSheet>()
            .ToArray();
        return new TemplateLibrary(sheets, blocks);
    }

    private static TemplateSheet? TryInferSheet(TemplateBlockDefinition block)
    {
        if (block.Entities.Count == 0)
            return null;

        var name = block.Name.Replace('_', '-');
        var format = Enum.GetValues(typeof(StandardSheetFormat)).Cast<StandardSheetFormat>()
            .Where(value => value != StandardSheetFormat.Unknown)
            .FirstOrDefault(value => name.IndexOf(value.ToString(), StringComparison.OrdinalIgnoreCase) >= 0);
        if (format == StandardSheetFormat.Unknown) return null;

        var orientation = name.IndexOf("landscape", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("альбом", StringComparison.OrdinalIgnoreCase) >= 0
            ? SheetOrientation.Landscape
            : name.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0
              || name.IndexOf("книж", StringComparison.OrdinalIgnoreCase) >= 0
                ? SheetOrientation.Portrait
                : SheetOrientation.Unknown;
        return orientation == SheetOrientation.Unknown ? null : new TemplateSheet(block.Name, format, orientation);
    }

    private sealed class ManifestDto
    {
        public string? SchemaVersion { get; set; }
        public List<BlockDto>? Blocks { get; set; }
    }

    private sealed class BlockDto
    {
        public string? Name { get; set; }
        public List<EntityDto>? Entities { get; set; }
        public List<AttributeDto>? Attributes { get; set; }
        public PointDto? Origin { get; set; }
    }

    private sealed class EntityDto
    {
        public string? ObjectClass { get; set; }
        public string? Handle { get; set; }
        public List<PointDto>? Points { get; set; }
        public string? Text { get; set; }
        public double? TextHeight { get; set; }
        public string? Layer { get; set; }
        public bool? IsClosed { get; set; }
        public double? RotationRadians { get; set; }
        public double? ArcRadius { get; set; }
        public double? StartAngleRadians { get; set; }
        public double? EndAngleRadians { get; set; }
    }

    private sealed class PointDto { public double X { get; set; } public double Y { get; set; } }
    private sealed class AttributeDto { public string? Tag { get; set; } public string? Prompt { get; set; } public string? DefaultValue { get; set; } }
}
