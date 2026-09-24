using System.Text.Json;
using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Generation;
using TeyPdfCad.TestGenerator.Pipelines;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class DiagnosticCliTests
{

    [Fact]
    public async Task P0SuppressionSafetyGate_PassesCleanCurrentBranchEvidence()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "teypdfcad-p0-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var corePath = Path.Combine(directory, "core.json");
            var diagnosticPath = Path.Combine(directory, "diagnostic.json");
            var gatePath = Path.Combine(directory, "gate.json");

            var core = new RegressionReport
            {
                Seed = 12345,
                Total = 1,
                ReleaseGate = new ReleaseGateResult
                {
                    Status = "PASS"
                }
            };
            var diagnostic = new DiagnosticRegressionReport
            {
                Seed = 12345,
                Total = 1,
                FalsePositive = 0,
                Cases =
                [
                    new DiagnosticCaseRecord
                    {
                        Expected = new DimensionCase
                        {
                            ExpectedResult = ExpectedResult.Recognized
                        },
                        Actual = new ActualDimensionResult
                        {
                            Result = ExpectedResult.Recognized
                        }
                    }
                ]
            };

            await File.WriteAllTextAsync(
                corePath,
                JsonDefaults.Serialize(core));
            await File.WriteAllTextAsync(
                diagnosticPath,
                JsonDefaults.Serialize(diagnostic));

            var exitCode = await TestGenerator.Program.Main(
            [
                "p0-suppression-safety-gate",
                "--core-report", corePath,
                "--diagnostic-report", diagnosticPath,
                "--expected-count", "1",
                "--output", gatePath
            ]);

            Assert.Equal(0, exitCode);
            using var gate = JsonDocument.Parse(
                await File.ReadAllTextAsync(gatePath));
            Assert.Equal(
                "PASS",
                gate.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task P0SuppressionSafetyGate_FailsWhenAmbiguousEvidenceIsForcedIntoRecognition()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "teypdfcad-p0-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var corePath = Path.Combine(directory, "core.json");
            var diagnosticPath = Path.Combine(directory, "diagnostic.json");
            var gatePath = Path.Combine(directory, "gate.json");

            var core = new RegressionReport
            {
                Seed = 12345,
                Total = 1,
                ReleaseGate = new ReleaseGateResult
                {
                    Status = "PASS"
                }
            };
            var diagnostic = new DiagnosticRegressionReport
            {
                Seed = 12345,
                Total = 1,
                FalsePositive = 0,
                Cases =
                [
                    new DiagnosticCaseRecord
                    {
                        Expected = new DimensionCase
                        {
                            ExpectedResult = ExpectedResult.Ambiguous
                        },
                        Actual = new ActualDimensionResult
                        {
                            Result = ExpectedResult.Recognized
                        }
                    }
                ]
            };

            await File.WriteAllTextAsync(
                corePath,
                JsonDefaults.Serialize(core));
            await File.WriteAllTextAsync(
                diagnosticPath,
                JsonDefaults.Serialize(diagnostic));

            var exitCode = await TestGenerator.Program.Main(
            [
                "p0-suppression-safety-gate",
                "--core-report", corePath,
                "--diagnostic-report", diagnosticPath,
                "--expected-count", "1",
                "--output", gatePath
            ]);

            Assert.Equal(2, exitCode);
            using var gate = JsonDocument.Parse(
                await File.ReadAllTextAsync(gatePath));
            Assert.Equal(
                "FAIL",
                gate.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                1,
                gate.RootElement
                    .GetProperty("forcedAmbiguousRecognition")
                    .GetInt32());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReconstructionQualityGate_FailsWhenRecallAndFullPassAreMateriallyDegraded()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "teypdfcad-reconstruction-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var corePath = Path.Combine(directory, "core.json");
            var gatePath = Path.Combine(directory, "gate.json");

            var core = new RegressionReport
            {
                Seed = 12345,
                Total = 100,
                Precision = 1.0,
                Recall = 0.80,
                F1 = 0.888,
                PassRate = 0.50,
                ReleaseGate = new ReleaseGateResult
                {
                    Status = "PASS"
                }
            };

            await File.WriteAllTextAsync(
                corePath,
                JsonDefaults.Serialize(core));

            var exitCode = await TestGenerator.Program.Main(
            [
                "reconstruction-quality-gate",
                "--core-report", corePath,
                "--expected-count", "100",
                "--output", gatePath
            ]);

            Assert.Equal(2, exitCode);
            using var gate = JsonDocument.Parse(
                await File.ReadAllTextAsync(gatePath));
            Assert.Equal(
                "FAIL",
                gate.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "reconstruction-quality",
                gate.RootElement.GetProperty("gateType").GetString());

            var reasons = gate.RootElement.GetProperty("reasons")
                .EnumerateArray()
                .Select(element => element.GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains(reasons, reason =>
                reason.Contains("Recall", StringComparison.Ordinal));
            Assert.Contains(reasons, reason =>
                reason.Contains("Full semantic pass rate", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReconstructionQualityGate_PassesOnlyWhenAbsoluteFloorsAreMet()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "teypdfcad-reconstruction-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var corePath = Path.Combine(directory, "core.json");
            var gatePath = Path.Combine(directory, "gate.json");

            var core = new RegressionReport
            {
                Seed = 12345,
                Total = 100,
                Precision = 0.999,
                Recall = 0.96,
                F1 = 0.97,
                PassRate = 0.92,
                ReleaseGate = new ReleaseGateResult
                {
                    Status = "PASS"
                }
            };

            await File.WriteAllTextAsync(
                corePath,
                JsonDefaults.Serialize(core));

            var exitCode = await TestGenerator.Program.Main(
            [
                "reconstruction-quality-gate",
                "--core-report", corePath,
                "--expected-count", "100",
                "--output", gatePath
            ]);

            Assert.Equal(0, exitCode);
            using var gate = JsonDocument.Parse(
                await File.ReadAllTextAsync(gatePath));
            Assert.Equal(
                "PASS",
                gate.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratedChainCase_000047_does_not_collapse_to_half_scale()
    {
        var testCase = new DimensionCaseGenerator()
            .Generate(47, 12345)
            .Cases[46];

        var run = await new SemanticCoreTestPipeline().RunDetailedAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, run.Actual.Result);
        Assert.Equal(testCase.ExpectedDimensions, run.Actual.DetectedDimensions);
        Assert.Equal(testCase.ExpectedValue, run.Actual.Value!.Value, 6);
        Assert.Equal(testCase.DrawingScale, run.Actual.DrawingScale!.Value, 6);
        Assert.Equal(DimensionType.Chain, run.Actual.DimensionType);
    }

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
