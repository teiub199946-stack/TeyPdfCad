using System.Text.Json;
using TeyPdfCad.TestGenerator;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class CoreRunCommandTests
{
    [Fact]
    public async Task CoreRun_Writes_Real_Core_Report_Without_Requiring_Accepted_Baseline()
    {
        var output = Path.Combine(Path.GetTempPath(), "teypdfcad-core-run-" + Guid.NewGuid().ToString("N"));

        try
        {
            var exitCode = await Program.Main(new[]
            {
                "core-run",
                "--count", "10",
                "--seed", "12345",
                "--output", output
            });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(output, "cases.json")));
            Assert.True(File.Exists(Path.Combine(output, "report.json")));
            Assert.True(File.Exists(Path.Combine(output, "report.md")));

            var report = JsonSerializer.Deserialize<RegressionReport>(
                await File.ReadAllTextAsync(Path.Combine(output, "report.json")),
                JsonDefaults.Options);

            Assert.NotNull(report);
            Assert.Equal(10, report!.Total);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public void MarkdownReport_Includes_Semantic_Type_Ambiguity_Metric()
    {
        var markdown = ArtifactWriter.BuildMarkdownReport(new RegressionReport
        {
            SemanticTypeAmbiguity = 7,
            ReleaseGate = new ReleaseGateResult { Status = "FAIL" }
        });

        Assert.Contains("Semantic type ambiguity", markdown, StringComparison.Ordinal);
        Assert.Contains("| Semantic type ambiguity | 7 |", markdown, StringComparison.Ordinal);
    }
}
