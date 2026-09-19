using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TeyPdfCad.AutoCAD;

public sealed record TemplateLibraryManifest(
    string SchemaVersion,
    IReadOnlyList<TemplateBlockManifest> Blocks,
    IReadOnlyList<TemplateStyleManifest> Styles,
    IReadOnlyList<string> UnsupportedEntityClasses)
{
    public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented, SerializerOptions);

    internal static readonly JsonSerializerSettings SerializerOptions = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };
}

public sealed record TemplateBlockManifest(
    string Name,
    IReadOnlyList<TemplateEntityManifest> Entities,
    IReadOnlyList<TemplateAttributeManifest> Attributes);

public sealed record TemplateEntityManifest(
    string ObjectClass,
    string Handle,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    IReadOnlyList<TemplatePointManifest>? Points = null,
    string? Text = null,
    double? TextHeight = null,
    string? Layer = null);

public sealed record TemplatePointManifest(double X, double Y);

public sealed record TemplateAttributeManifest(string Tag, string Prompt, string DefaultValue);

public sealed record TemplateStyleManifest(string Name, string ObjectClass);
