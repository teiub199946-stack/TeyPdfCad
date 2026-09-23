using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Dwg;

internal sealed class AcadSharpStyleCatalog
{
    private readonly CadDocument _document;
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LineType> _lineTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DimensionStyle> _dimensionStyles = new(StringComparer.Ordinal);
    private TextStyle? _pdfTextStyle;

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
            entity.Color = rgbColor == 0
                ? new Color(7)
                : Color.FromTrueColor((uint)rgbColor);
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

    public Layer GetAnnotationLayer(string name) => GetLayer(name);

    public TextStyle GetPdfTextStyle()
    {
        if (_pdfTextStyle is not null) return _pdfTextStyle;
        const string name = "TEYPDFCAD_TEXT";
        if (!_document.TextStyles.TryGetValue(name, out var style))
        {
            style = new TextStyle(name)
            {
                Filename = "arial.ttf",
                Height = 0d,
                Width = 1d
            };
            _document.TextStyles.Add(style);
        }
        return _pdfTextStyle = style;
    }

    public DimensionStyle GetDimensionStyle(DimensionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var tickSize = TryGetObliqueTickSize(candidate.SourceAppearance);
        return GetDimensionStyle(candidate.DrawingScale, tickSize);
    }

    public DimensionStyle GetDimensionStyle(double linearScale)
        => GetDimensionStyle(linearScale, tickSize: null);

    private DimensionStyle GetDimensionStyle(double linearScale, double? tickSize)
    {
        var canonicalScale = Math.Round(linearScale, 6);
        var scaleToken = canonicalScale.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_');
        var tickToken = tickSize.HasValue
            ? "_TICK_" + Math.Round(tickSize.Value, 6)
                .ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                .Replace('.', '_')
            : string.Empty;
        var name = $"TEYPDFCAD_SCALE_{scaleToken}{tickToken}";
        if (_dimensionStyles.TryGetValue(name, out var cached)) return cached;
        if (!_document.DimensionStyles.TryGetValue(name, out var style))
        {
            style = new DimensionStyle(name)
            {
                LinearScaleFactor = canonicalScale,
                TextHeight = 2.5d,
                ArrowSize = 2.5d,
                TickSize = tickSize ?? 0d,
                DimensionLineExtension = 0d,
                ExtensionLineOffset = 0.75d,
                ExtensionLineExtension = 1.25d,
                ScaleFactor = 1d,
                Style = GetPdfTextStyle()
            };
            _document.DimensionStyles.Add(style);
        }
        _dimensionStyles.Add(name, style);
        return style;
    }

    private static double? TryGetObliqueTickSize(DimensionSourceAppearance? appearance)
    {
        if (appearance is null || appearance.ArrowLines.Count != 2)
            return null;

        var dimensionVector = GeometryMath.Subtract(
            appearance.DimensionLine.End,
            appearance.DimensionLine.Start);
        if (GeometryMath.Length(dimensionVector) <= 1e-9)
            return null;
        var unitDimension = GeometryMath.Normalize(dimensionVector);
        var endpoints = new[] { appearance.DimensionLine.Start, appearance.DimensionLine.End };
        var lengths = new List<double>(2);

        foreach (var endpoint in endpoints)
        {
            var touching = appearance.ArrowLines
                .Where(line => IsSupportedObliqueTick(line, endpoint, unitDimension, appearance.Text.HeightMm))
                .ToArray();
            if (touching.Length != 1)
                return null;
            lengths.Add(GeometryMath.Distance(touching[0].Start, touching[0].End));
        }

        if (Math.Abs(lengths[0] - lengths[1]) > Math.Max(1e-6, Math.Max(lengths[0], lengths[1]) * 1e-3))
            return null;

        return (lengths[0] + lengths[1]) / 2d;
    }

    private static bool IsSupportedObliqueTick(
        DimensionSourceLineAppearance line,
        Point2 endpoint,
        Point2 unitDimension,
        double textHeight)
    {
        var vector = GeometryMath.Subtract(line.End, line.Start);
        var length = GeometryMath.Length(vector);
        if (length <= 1e-9)
            return false;

        var tolerance = Math.Max(Math.Min(textHeight * 0.05d, 0.1d), 1e-6);
        if (GeometryMath.DistancePointToSegment(endpoint, line.Start, line.End) > tolerance)
            return false;

        var projection = ProjectionParameter(endpoint, line.Start, line.End);
        if (projection is <= 0.05d or >= 0.95d)
            return false;

        var cosine = Math.Abs(GeometryMath.Dot(GeometryMath.Normalize(vector), unitDimension));
        return Math.Abs(cosine - Math.Sqrt(0.5d)) <= 0.01d;
    }

    private static double ProjectionParameter(Point2 point, Point2 start, Point2 end)
    {
        var vector = GeometryMath.Subtract(end, start);
        var denominator = GeometryMath.Dot(vector, vector);
        return denominator <= 1e-12
            ? 0d
            : GeometryMath.Dot(GeometryMath.Subtract(point, start), vector) / denominator;
    }

    public LineType GetCenterLineType()
    {
        const string name = "TEYPDFCAD_CENTER";
        if (_document.LineTypes.TryGetValue(name, out var existing)) return existing;
        var lineType = new LineType(name) { Description = "PDF reconstructed axis" };
        lineType.AddSegment(new LineType.Segment { Length = 12d });
        lineType.AddSegment(new LineType.Segment { Length = -3d });
        lineType.AddSegment(new LineType.Segment { Length = 2d });
        lineType.AddSegment(new LineType.Segment { Length = -3d });
        _document.LineTypes.Add(lineType);
        return lineType;
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
                var length = Math.Abs(pattern[index]) * VectorPdfPage.MillimetresPerPoint;
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
