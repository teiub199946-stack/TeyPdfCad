using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using TeyPdfCad.Core.Documents;

namespace TeyPdfCad.Dwg;

internal sealed class AcadSharpStyleCatalog
{
    private readonly CadDocument _document;
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LineType> _lineTypes = new(StringComparer.Ordinal);

    public AcadSharpStyleCatalog(CadDocument document)
    {
        _document = document;
    }

    public void Apply(Entity entity, VectorStyle style)
    {
        var layerName = string.IsNullOrWhiteSpace(style.SourceLayer) ? Layer.Default.Name : style.SourceLayer;
        entity.Layer = GetLayer(layerName);

        if (style.RgbColor is { } rgbColor)
        {
            entity.Color = Color.FromTrueColor((uint)rgbColor);
        }

        if (style.DashPatternPoints is { Count: > 0 } dashPattern)
        {
            entity.LineType = GetDashLineType(dashPattern);
        }

        if (style.StrokeWidthPoints is { } strokeWidth)
        {
            entity.LineWeight = GetLineWeight(strokeWidth);
        }
    }

    private Layer GetLayer(string name)
    {
        if (_layers.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!_document.Layers.TryGetValue(name, out var layer))
        {
            layer = new Layer(name);
            _document.Layers.Add(layer);
        }

        _layers.Add(name, layer);
        return layer;
    }

    private LineType GetDashLineType(IReadOnlyList<double> pattern)
    {
        var canonicalPattern = pattern.Select(value => Math.Abs(value).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        var name = $"PDF_DASH_{string.Join("_", canonicalPattern)}";
        if (_lineTypes.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!_document.LineTypes.TryGetValue(name, out var lineType))
        {
            lineType = new LineType(name)
            {
                Description = "PDF stroke dash pattern"
            };
            for (var index = 0; index < pattern.Count; index++)
            {
                var length = Math.Abs(pattern[index]);
                lineType.AddSegment(new LineType.Segment
                {
                    Length = index % 2 == 0 ? length : -length
                });
            }
            _document.LineTypes.Add(lineType);
        }

        _lineTypes.Add(name, lineType);
        return lineType;
    }

    private static LineWeightType GetLineWeight(double points)
    {
        var targetHundredthsOfMillimetre = points * VectorPdfPage.MillimetresPerPoint * 100d;
        return Enum.GetValues<LineWeightType>()
            .Where(value => value is not LineWeightType.ByBlock and not LineWeightType.ByLayer and not LineWeightType.ByDIPs and not LineWeightType.Default)
            .MinBy(value => Math.Abs((int)value - targetHundredthsOfMillimetre));
    }
}
