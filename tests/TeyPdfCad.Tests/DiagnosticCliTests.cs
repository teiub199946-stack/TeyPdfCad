using System.Text.Json;
using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class DiagnosticCliTests
{
    [Fact]
    public async Task DiagnoseCore_WritesDedicatedTest003Artifacts()
    {
        var output = Path.Combine(Path.GetTempPath(), "teypdfcad-test003-" + Guid.NewGuid().ToString("N"));
        try
        {
            var exitCode = await TestGenerator.Program.Main(new[]
            {
                "diagnose-core",
                "--count", "20",
                "--seed", "12345",
                "--output", output
            });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(output, "report.json")));
            Assert.True(File.Exists(Path.Combine(output, "report.md")));
            Assert.True(File.Exists(Path.Combine(output, "worst_cases.json")));

            var report = JsonSerializer.Deserialize<DiagnosticRegressionReport>(
                await File.ReadAllTextAsync(Path.Combine(output, "report.json")),
                JsonDefaults.Options);
            var worst = JsonSerializer.Deserialize<List<WorstCaseRecord>>(
                await File.ReadAllTextAsync(Path.Combine(output, "worst_cases.json")),
                JsonDefaults.Options);
            var markdown = await File.ReadAllTextAsync(Path.Combine(output, "report.md"));

            Assert.NotNull(report);
            Assert.NotNull(worst);
            Assert.Equal(20, report!.Total);
            Assert.True(worst!.Count <= 20);
            Assert.DoesNotContain(worst, x => x.Category is
                DiagnosticCategory.ExpectedNoisePropagation or DiagnosticCategory.NumericTolerance);

            Assert.Contains("Legacy WrongPoints split", markdown, StringComparison.Ordinal);
            Assert.Contains("Detection Precision", markdown, StringComparison.Ordinal);
            Assert.Contains("Correct Geometry", markdown, StringComparison.Ordinal);
            Assert.Contains("Abstention accuracy", markdown, StringComparison.Ordinal);
            Assert.Contains("Paper-space error percentiles", markdown, StringComparison.Ordinal);
            Assert.Contains("World-space error percentiles", markdown, StringComparison.Ordinal);
            Assert.Contains("Cohorts", markdown, StringComparison.Ordinal);
            Assert.Contains("Scale systematic error", markdown, StringComparison.Ordinal);
            Assert.Contains("Signed point bias", markdown, StringComparison.Ordinal);
            Assert.Contains("Top 20 real Core defects", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }
}
