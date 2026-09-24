using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TeyPdfCad.AutoCAD;

internal sealed record DimensionTextFragmentMetric(
    string Text,
    string TrueTypeFont,
    string ShxFont,
    double ExtentWidth,
    double ExtentHeight,
    double CapsHeight,
    double TrackingFactor,
    double WidthFactor,
    double ObliqueAngle,
    double LocationX,
    double LocationY,
    double LocationZ,
    double DirectionX,
    double DirectionY,
    double DirectionZ,
    bool Bold,
    bool Italic,
    bool StackTop,
    bool StackBottom,
    bool Underlined,
    bool Overlined,
    bool Strikethrough);

internal sealed record DimensionTextMetric(
    string EntityType,
    string EntityHandle,
    string Text,
    string MetricKind,
    double Width,
    double Height,
    double RotationRadians,
    double PositionX,
    double PositionY,
    double PositionZ,
    string TextStyleName,
    string FontFile,
    double TextStyleWidthFactor,
    string FontResolvedPath = "",
    string FontSha256 = "",
    double NominalTextHeight = 0d,
    double EntityWidthFactor = 1d,
    bool BackgroundFill = false,
    bool UseBackgroundColor = false,
    double BackgroundScaleFactor = 0d,
    bool ShowBorders = false,
    string Attachment = "",
    IReadOnlyList<DimensionTextFragmentMetric>? Fragments = null);

internal sealed record DimensionBlockGeometryMetric(
    string EntityType,
    string EntityHandle,
    string GeometryKind,
    double? StartX,
    double? StartY,
    double? StartZ,
    double? EndX,
    double? EndY,
    double? EndZ,
    double? MinX,
    double? MinY,
    double? MinZ,
    double? MaxX,
    double? MaxY,
    double? MaxZ,
    string NestedBlockName,
    int VertexCount,
    string ColorMethod = "",
    int? RgbColor = null,
    string LineWeightMode = "",
    int? LineWeightHundredthsMm = null,
    string Linetype = "",
    string Layer = "");

internal sealed record DimensionMetric(
    string DimensionHandle,
    string DimensionType,
    double Measurement,
    string DimensionText,
    string DimensionBlockHandle,
    IReadOnlyList<DimensionTextMetric> TextMetrics,
    string? Error = null,
    string CandidateId = "",
    string CandidateRole = "",
    IReadOnlyList<DimensionBlockGeometryMetric>? BlockGeometry = null,
    IReadOnlyList<DimensionBlockGeometryMetric>? ExplodedGeometry = null,
    IReadOnlyList<DimensionTextMetric>? ExplodedTextMetrics = null,
    string BlockGeometryCoordinateFrame = "dimension-block-mcs",
    string ExplodedGeometryCoordinateFrame = "drawing-wcs");

internal sealed record DimensionMetricsReport(
    string SchemaVersion,
    string DrawingName,
    string DrawingUnits,
    IReadOnlyList<DimensionMetric> Dimensions);

internal static class DimensionMetricsReportFormatter
{
    public const string SchemaVersion = "9";

    public static string Format(DimensionMetricsReport report)
    {
        if (report is null)
            throw new ArgumentNullException(nameof(report));

        var normalized = report with
        {
            Dimensions = report.Dimensions
                .OrderBy(item => item.DimensionHandle, StringComparer.Ordinal)
                .Select(item => item with
                {
                    TextMetrics = item.TextMetrics
                        .OrderBy(metric => metric.EntityHandle, StringComparer.Ordinal)
                        .ThenBy(metric => metric.EntityType, StringComparer.Ordinal)
                        .Select(metric => metric with
                        {
                            Fragments = (metric.Fragments ?? [])
                                .OrderBy(fragment => fragment.LocationY)
                                .ThenBy(fragment => fragment.LocationX)
                                .ThenBy(fragment => fragment.Text, StringComparer.Ordinal)
                                .ToArray()
                        })
                        .ToArray(),
                    BlockGeometry = (item.BlockGeometry ?? [])
                        .OrderBy(entity => entity.EntityType, StringComparer.Ordinal)
                        .ThenBy(entity => entity.EntityHandle, StringComparer.Ordinal)
                        .ToArray(),
                    ExplodedGeometry = (item.ExplodedGeometry ?? [])
                        .OrderBy(entity => entity.EntityType, StringComparer.Ordinal)
                        .ThenBy(entity => entity.EntityHandle, StringComparer.Ordinal)
                        .ToArray(),
                    ExplodedTextMetrics = (item.ExplodedTextMetrics ?? [])
                        .OrderBy(metric => metric.EntityHandle, StringComparer.Ordinal)
                        .ThenBy(metric => metric.EntityType, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToArray()
        };

        return JsonConvert.SerializeObject(
            normalized,
            Formatting.Indented,
            new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
    }
}

internal static class DimensionMetricsFileIdentity
{
    public static string ComputeSha256(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return string.Empty;

        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha256.ComputeHash(stream))
                .Replace("-", string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
