using System.Globalization;
using Newtonsoft.Json;

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
    double TextStyleWidthFactor);

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
    public const string SchemaVersion = "1";

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
                NullValueHandling = NullValueHandling.Include
            });
    }
}
