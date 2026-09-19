using Newtonsoft.Json;

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
                    entity.Layer)).ToArray(),
                (block.Attributes ?? []).Select(attribute => new TemplateAttributeDefinition(
                    attribute.Tag ?? string.Empty,
                    attribute.Prompt ?? string.Empty,
                    attribute.DefaultValue ?? string.Empty)).ToArray()))
            .ToArray();
        return new TemplateLibrary([], blocks);
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
    }

    private sealed class EntityDto
    {
        public string? ObjectClass { get; set; }
        public string? Handle { get; set; }
        public List<PointDto>? Points { get; set; }
        public string? Text { get; set; }
        public double? TextHeight { get; set; }
        public string? Layer { get; set; }
    }

    private sealed class PointDto { public double X { get; set; } public double Y { get; set; } }
    private sealed class AttributeDto { public string? Tag { get; set; } public string? Prompt { get; set; } public string? DefaultValue { get; set; } }
}
