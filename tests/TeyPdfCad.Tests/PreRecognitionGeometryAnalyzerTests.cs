using System.Text.Json;
using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
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

    [Fact]
    public async Task Preflight_is_byte_stable_and_preserves_generation_contract()
    {
        const int count = 100;
        const int seed = 12345;
        var firstOutput = Path.Combine(Path.GetTempPath(), "teypdfcad-preflight-a-" + Guid.NewGuid().ToString("N"));
        var secondOutput = Path.Combine(Path.GetTempPath(), "teypdfcad-preflight-b-" + Guid.NewGuid().ToString("N"));
        var corpus = new DimensionCaseGenerator().Generate(count, seed);
        var originalCorpusJson = JsonSerializer.Serialize(corpus);

        try
        {
            Assert.Equal(0, await TestGenerator.Program.Main(
            [
                "preflight-geometry", "--count", count.ToString(),
                "--seed", seed.ToString(), "--output", firstOutput
            ]));
            Assert.Equal(0, await TestGenerator.Program.Main(
            [
                "preflight-geometry", "--count", count.ToString(),
                "--seed", seed.ToString(), "--output", secondOutput
            ]));

            Assert.Equal(originalCorpusJson, JsonSerializer.Serialize(corpus));

            var firstJson = await File.ReadAllTextAsync(Path.Combine(firstOutput, "preflight_geometry.json"));
            var secondJson = await File.ReadAllTextAsync(Path.Combine(secondOutput, "preflight_geometry.json"));
            Assert.Equal(firstJson, secondJson);

            using var report = JsonDocument.Parse(firstJson);
            var root = report.RootElement;
            var valid = root.GetProperty("valid").GetInt32();
            var crowded = root.GetProperty("crowded").GetInt32();
            var degenerate = root.GetProperty("degenerate").GetInt32();
            Assert.Equal(count, root.GetProperty("total").GetInt32());
            Assert.Equal(count, valid + crowded + degenerate);

            var expectedStatuses = corpus.Cases
                .Select(item => new
                {
                    item.Id,
                    Status = PreRecognitionGeometryAnalyzer.Analyze(item).Status.ToString()
                })
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            var actualStatuses = root.GetProperty("cases")
                .EnumerateArray()
                .Select(item => new
                {
                    Id = item.GetProperty("id").GetString(),
                    Status = item.GetProperty("status").GetString()
                })
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedStatuses.Select(item => item.Id), actualStatuses.Select(item => item.Id));
            Assert.Equal(expectedStatuses.Select(item => item.Status), actualStatuses.Select(item => item.Status));
        }
        finally
        {
            if (Directory.Exists(firstOutput))
                Directory.Delete(firstOutput, recursive: true);
            if (Directory.Exists(secondOutput))
                Directory.Delete(secondOutput, recursive: true);
        }
    }

}
