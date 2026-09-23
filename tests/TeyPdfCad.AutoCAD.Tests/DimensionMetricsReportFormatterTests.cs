using Newtonsoft.Json;
using TeyPdfCad.AutoCAD;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class DimensionMetricsReportFormatterTests
{
    [Fact]
    public void Formatter_is_deterministic_and_orders_dimensions_and_text_entities()
    {
        var report = new DimensionMetricsReport(
            DimensionMetricsReportFormatter.SchemaVersion,
            "sample.dwg",
            "Millimeters",
            [
                new DimensionMetric(
                    "20",
                    "RotatedDimension",
                    200,
                    "200",
                    "B20",
                    [
                        Metric("F2", "MText", 12.5, 3.2),
                        Metric("A2", "DBText", 11.5, 3.0)
                    ]),
                new DimensionMetric(
                    "10",
                    "AlignedDimension",
                    100,
                    "100",
                    "B10",
                    [Metric("C1", "MText", 8.25, 2.5)])
            ]);

        var first = DimensionMetricsReportFormatter.Format(report);
        var second = DimensionMetricsReportFormatter.Format(report);

        Assert.Equal(first, second);
        var parsed = JsonConvert.DeserializeObject<DimensionMetricsReport>(first);
        Assert.NotNull(parsed);
        Assert.Equal(["10", "20"], parsed!.Dimensions.Select(item => item.DimensionHandle).ToArray());
        Assert.Equal(
            ["A2", "F2"],
            parsed.Dimensions[1].TextMetrics.Select(item => item.EntityHandle).ToArray());
        Assert.Contains("\"schemaVersion\": \"1\"", first, StringComparison.Ordinal);
        Assert.Contains("\"width\": 8.25", first, StringComparison.Ordinal);
    }

    private static DimensionTextMetric Metric(
        string handle,
        string type,
        double width,
        double height)
        => new(
            type,
            handle,
            "100",
            type == "MText" ? "mtext-actual-bounds" : "dbtext-geometric-extents",
            width,
            height,
            0,
            1,
            2,
            0,
            "TEYPDFCAD_TEXT",
            "arial.ttf",
            1);
}
