using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Generation;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class PreRecognitionGeometryAnalyzerTests
{
    [Fact]
    public void Chain_case_000010_is_degenerate_before_recognition()
    {
        var testCase = new DimensionCaseGenerator()
            .Generate(10_000, 12345)
            .Cases
            .Single(item => item.Id == "case_000010");

        var assessment = PreRecognitionGeometryAnalyzer.Analyze(testCase);

        Assert.Equal(PreRecognitionGeometryStatus.Degenerate, assessment.Status);
        Assert.Equal(2, assessment.DegenerateSegments.Count);
        Assert.All(
            assessment.DegenerateSegments,
            segment => Assert.Contains("post-mismatch", segment.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preflight_geometry_writes_validity_counts_without_changing_release_gates()
    {
        var output = Path.Combine(
            Path.GetTempPath(),
            "teypdfcad-preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            var exitCode = await TestGenerator.Program.Main(
            [
                "preflight-geometry",
                "--count", "20",
                "--seed", "12345",
                "--output", output
            ]);

            Assert.Equal(0, exitCode);
            var json = await File.ReadAllTextAsync(
                Path.Combine(output, "preflight_geometry.json"));
            Assert.Contains("\"total\": 20", json, StringComparison.Ordinal);
            Assert.Contains("\"degenerate\"", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

}
