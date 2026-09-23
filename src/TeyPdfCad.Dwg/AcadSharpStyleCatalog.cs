using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        return GetDimensionStyle(candidate.DrawingScale, tickSize, candidate.SourceAppearance);
    }

    public DimensionStyle GetDimensionStyle(double linearScale)
        => GetDimensionStyle(linearScale, tickSize: null, appearance: null);

    private DimensionStyle GetDimensionStyle(
        double linearScale,
        double? tickSize,
        DimensionSourceAppearance? appearance)
    {
        var canonicalScale = Math.Round(linearScale, 6);
        var scaleToken = canonicalScale.ToString("0.######", CultureInfo.InvariantCulture).Replace('.', '_');
        var tickToken = tickSize.HasValue
            ? "_TICK_" + Math.Round(tickSize.Value, 6)
                .ToString("0.######", CultureInfo.InvariantCulture)
                .Replace('.', '_')
            : string.Empty;
        var appearanceToken = appearance is null
            ? string.Empty
            : "_SRC_" + BuildDimensionAppearanceToken(appearance);
        var name = $"TEYPDFCAD_SCALE_{scaleToken}{tickToken}{appearanceToken}";

        if (_dimensionStyles.TryGetValue(name, out var cached))
            return cached;

        if (!_document.DimensionStyles.TryGetValue(name, out var style))
        {
            style = new DimensionStyle(name)
            {
                LinearScaleFactor = canonicalScale,
                TextHeight = GetTextHeight(appearance),
                ArrowSize = 2.5d,
                TickSize = tickSize ?? 0d,
                DimensionLineExtension = 0d,
                ExtensionLineOffset = appearance is null ? 0.75d : 0d,
                ExtensionLineExtension = appearance is null
                    ? 1.25d
                    : GetExtensionBeyondDimensionLine(appearance) ?? 0d,
                ScaleFactor = 1d,
                Style = GetPdfTextStyle()
            };

            if (appearance is not null)
                ApplyDimensionAppearance(style, appearance);

            _document.DimensionStyles.Add(style);
        }

        _dimensionStyles.Add(name, style);
        return style;
    }

    private void ApplyDimensionAppearance(
        DimensionStyle style,
        DimensionSourceAppearance appearance)
    {
        style.DimensionLineColor = PdfColor(appearance.DimensionLine.RgbColor);
        style.TextColor = PdfColor(appearance.Text.RgbColor);

        if (appearance.ExtensionLines.Count == 2)
            style.ExtensionLineColor = PdfColor(appearance.ExtensionLines[0].RgbColor);

        if (appearance.DimensionLine.StrokeWidthMm is { } dimensionWidth)
            style.DimensionLineWeight = GetLineWeightMillimetres(dimensionWidth);

        if (appearance.ExtensionLines.Count == 2
            && appearance.ExtensionLines[0].StrokeWidthMm is { } extensionWidth)
        {
            style.ExtensionLineWeight = GetLineWeightMillimetres(extensionWidth);
        }

        style.LineType = GetDimensionLineType(appearance.DimensionLine.DashPatternMm);
        if (appearance.ExtensionLines.Count == 2)
        {
            style.LineTypeExt1 = GetDimensionLineType(appearance.ExtensionLines[0].DashPatternMm);
            style.LineTypeExt2 = GetDimensionLineType(appearance.ExtensionLines[1].DashPatternMm);
        }
    }

    private LineType GetDimensionLineType(IReadOnlyList<double> patternMm)
    {
        var normalized = NormalizePdfDashPattern(patternMm);
        if (normalized.Count == 0)
            return _document.LineTypes.Continuous;

        var canonicalPattern = normalized.Select(value =>
            Math.Abs(value).ToString("0.######", CultureInfo.InvariantCulture));
        var name = $"PDF_DASH_MM_{string.Join("_", canonicalPattern)}";
        if (_lineTypes.TryGetValue(name, out var cached))
            return cached;

        if (!_document.LineTypes.TryGetValue(name, out var lineType))
        {
            lineType = new LineType(name)
            {
                Description = "PDF dimension source dash pattern"
            };
            for (var index = 0; index < normalized.Count; index++)
            {
                lineType.AddSegment(new LineType.Segment
                {
                    Length = index % 2 == 0
                        ? Math.Abs(normalized[index])
                        : -Math.Abs(normalized[index])
                });
            }
            _document.LineTypes.Add(lineType);
        }

        _lineTypes.Add(name, lineType);
        return lineType;
    }

    private static IReadOnlyList<double> NormalizePdfDashPattern(IReadOnlyList<double> pattern)
    {
        if (pattern.Count == 0)
            return [];

        var values = pattern
            .Select(value => Math.Abs(value))
            .ToArray();
        if (values.Length % 2 == 0)
            return values;

        return [..values, ..values];
    }

    private static double GetTextHeight(DimensionSourceAppearance? appearance)
    {
        var height = appearance?.Text.HeightMm;
        return height.HasValue && double.IsFinite(height.Value) && height.Value > 1e-9
            ? height.Value
            : 2.5d;
    }

    private static double? GetExtensionBeyondDimensionLine(DimensionSourceAppearance? appearance)
    {
        if (appearance is null || appearance.ExtensionLines.Count != 2)
            return null;

        var first = GetExtensionBeyondDimensionLine(
            appearance.ExtensionLines[0],
            appearance.DimensionLine);
        var second = GetExtensionBeyondDimensionLine(
            appearance.ExtensionLines[1],
            appearance.DimensionLine);
        if (!first.HasValue || !second.HasValue)
            return null;
        if (Math.Abs(first.Value - second.Value) > 1e-6)
            return null;
        return (first.Value + second.Value) / 2d;
    }

    private static double? GetExtensionBeyondDimensionLine(
        DimensionSourceLineAppearance extension,
        DimensionSourceLineAppearance dimensionLine)
    {
        var dimensionVector = GeometryMath.Subtract(dimensionLine.End, dimensionLine.Start);
        var extensionVector = GeometryMath.Subtract(extension.End, extension.Start);
        if (GeometryMath.Length(dimensionVector) <= 1e-9
            || GeometryMath.Length(extensionVector) <= 1e-9)
            return null;

        var firstSigned = SignedDistanceToLine(extension.Start, dimensionLine);
        var secondSigned = SignedDistanceToLine(extension.End, dimensionLine);
        if (Math.Abs(firstSigned) <= 1e-9 || Math.Abs(secondSigned) <= 1e-9)
            return 0d;
        if (Math.Sign(firstSigned) == Math.Sign(secondSigned))
            return null;

        return Math.Min(Math.Abs(firstSigned), Math.Abs(secondSigned));
    }

    private static double SignedDistanceToLine(
        Point2 point,
        DimensionSourceLineAppearance line)
    {
        var vector = GeometryMath.Subtract(line.End, line.Start);
        var length = GeometryMath.Length(vector);
        if (length <= 1e-12)
            return double.NaN;
        var relative = GeometryMath.Subtract(point, line.Start);
        return (vector.X * relative.Y - vector.Y * relative.X) / length;
    }

    private static Color PdfColor(int? rgb)
        => Color.FromTrueColor((uint)(rgb ?? 0));

    private static LineWeightType GetLineWeightMillimetres(double millimetres)
    {
        var targetHundredths = millimetres * 100d;
        return Enum.GetValues<LineWeightType>()
            .Where(value => value is not LineWeightType.ByBlock
                and not LineWeightType.ByLayer
                and not LineWeightType.ByDIPs
                and not LineWeightType.Default)
            .MinBy(value => Math.Abs((int)value - targetHundredths));
    }

    private static string BuildDimensionAppearanceToken(DimensionSourceAppearance appearance)
    {
        var canonical = string.Join("|",
            appearance.DimensionLine.RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black",
            appearance.DimensionLine.StrokeWidthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "null",
            DashToken(appearance.DimensionLine.DashPatternMm),
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[0].RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black"
                : "ext-color-unknown",
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[0].StrokeWidthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "null"
                : "ext-width-unknown",
            appearance.ExtensionLines.Count == 2
                ? DashToken(appearance.ExtensionLines[0].DashPatternMm)
                : "ext1-dash-unknown",
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[1].RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black"
                : "ext2-color-unknown",
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[1].StrokeWidthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "null"
                : "ext2-width-unknown",
            appearance.ExtensionLines.Count == 2
                ? DashToken(appearance.ExtensionLines[1].DashPatternMm)
                : "ext2-dash-unknown",
            appearance.Text.RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black",
            appearance.Text.HeightMm.ToString("R", CultureInfo.InvariantCulture),
            (GetExtensionBeyondDimensionLine(appearance) ?? 0d).ToString("R", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static string DashToken(IReadOnlyList<double> pattern)
        => string.Join(",", NormalizePdfDashPattern(pattern)
            .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

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
