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
                    [Metric("C1", "MText", 8.25, 2.5)],
                    BlockGeometry:
                    [
                        new DimensionBlockGeometryMetric(
                            "Line",
                            "L1",
                            "line",
                            0, 0, 0,
                            10, 0, 0,
                            0, 0, 0,
                            10, 0, 0,
                            string.Empty,
                            0)
                    ],
                    ExplodedGeometry:
                    [
                        new DimensionBlockGeometryMetric(
                            "Line",
                            "explode-0",
                            "line",
                            0, 0, 0,
                            10, 0, 0,
                            0, 0, 0,
                            10, 0, 0,
                            string.Empty,
                            0)
                    ])
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
        Assert.Contains("\"schemaVersion\": \"4\"", first, StringComparison.Ordinal);
        Assert.Contains("\"width\": 8.25", first, StringComparison.Ordinal);
        Assert.Contains("\"fontSha256\": \"\"", first, StringComparison.Ordinal);
        Assert.Contains("\"fragments\":", first, StringComparison.Ordinal);
        Assert.Contains("\"trackingFactor\": 1.0", first, StringComparison.Ordinal);
        Assert.Contains("\"blockGeometry\":", first, StringComparison.Ordinal);
        Assert.Contains("\"geometryKind\": \"line\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public void File_identity_uses_stable_uppercase_sha256()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.bin");
        try
        {
            File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("abc"));

            var hash = DimensionMetricsFileIdentity.ComputeSha256(path);

            Assert.Equal(
                "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                hash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
            1,
            Fragments: type == "MText"
                ?
                [
                    new DimensionTextFragmentMetric(
                        "100",
                        "Arial",
                        string.Empty,
                        width,
                        height,
                        height,
                        1d,
                        1d,
                        0d,
                        1d,
                        2d,
                        0d,
                        1d,
                        0d,
                        0d,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false)
                ]
                : []);
}
