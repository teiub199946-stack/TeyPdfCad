using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TeyPdfCad.AutoCAD;

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
    double EntityWidthFactor = 1d);

internal sealed record DimensionMetric(
    string DimensionHandle,
    string DimensionType,
    double Measurement,
    string DimensionText,
    string DimensionBlockHandle,
    IReadOnlyList<DimensionTextMetric> TextMetrics,
    string? Error = null);

internal sealed record DimensionMetricsReport(
    string SchemaVersion,
    string DrawingName,
    string DrawingUnits,
    IReadOnlyList<DimensionMetric> Dimensions);

internal static class DimensionMetricsReportFormatter
{
    public const string SchemaVersion = "2";

    public static string Format(DimensionMetricsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var normalized = report with
        {
            Dimensions = report.Dimensions
                .OrderBy(item => item.DimensionHandle, StringComparer.Ordinal)
                .Select(item => item with
                {
                    TextMetrics = item.TextMetrics
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

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha256.ComputeHash(stream))
            .Replace("-", string.Empty);
    }
}
