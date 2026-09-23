using System.Text.Json;
using TeyPdfCad.Cli;
using Xunit;

namespace TeyPdfCad.Cli.Tests;

public sealed class DimensionMetricComparatorTests
{
    [Fact]
    public void Comparator_joins_source_and_autocad_metrics_by_persistent_candidate_id()
    {
        var source = """
        {
          "pages": [
            {
              "dimensionMetricEvidence": [
                {
                  "candidateId": "p1:dimension:abc",
                  "sourceText": "100",
                  "sourceFontName": "Helvetica",
                  "sourceFontSubtype": "TrueType",
                  "sourceFontEncodingName": "WinAnsiEncoding",
                  "sourceFontHasToUnicode": false,
                  "sourceFontIsSubset": false,
                  "sourceAdvanceWidthMm": 8.0,
                  "sourceVisibleWidthMm": 7.5,
                  "sourceHeightMm": 2.5,
                  "sourceRotationDegrees": 0,
                  "sourceVisualCenterX": 15,
                  "sourceVisualCenterY": 20
                }
              ]
            }
          ]
        }
        """;
        var native = """
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [
            {
              "dimensionHandle": "10",
              "dimensionType": "RotatedDimension",
              "measurement": 100,
              "dimensionText": "100",
              "dimensionBlockHandle": "20",
              "candidateId": "p1:dimension:abc",
              "candidateRole": "primary",
              "textMetrics": [
                {
                  "entityType": "MText",
                  "entityHandle": "30",
                  "text": "100",
                  "metricKind": "mtext-actual-bounds-dimblock-mcs",
                  "width": 7.5,
                  "height": 2.5,
                  "rotationRadians": 0,
                  "positionX": 15,
                  "positionY": 20,
                  "positionZ": 0,
                  "textStyleName": "TEYPDFCAD_TEXT",
                  "fontFile": "arial.ttf",
                  "textStyleWidthFactor": 1,
                  "fontResolvedPath": "C:\\Windows\\Fonts\\arial.ttf",
                  "fontSha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "nominalTextHeight": 2.5,
                  "entityWidthFactor": 1
                }
              ]
            }
          ]
        }
        """;

        var report = DimensionMetricComparator.Compare(source, native);
        var candidate = Assert.Single(report.Candidates);

        Assert.Equal("p1:dimension:abc", candidate.CandidateId);
        Assert.True(candidate.MeasurementsAreUsable);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
        Assert.True(candidate.VisibleWidthDeltaMm.HasValue);
        Assert.Equal(0d, candidate.VisibleWidthDeltaMm.Value, 9);
        Assert.Equal(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            candidate.NativeFontSha256);
        Assert.Contains(
            "source-font-program-fingerprint-unavailable",
            candidate.Blockers);
    }

    [Fact]
    public void Comparator_reports_global_blocker_for_native_metric_without_candidate_id()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var native = """
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [
            {
              "dimensionHandle": "10",
              "dimensionType": "RotatedDimension",
              "measurement": 100,
              "dimensionText": "100",
              "dimensionBlockHandle": "20",
              "candidateId": "",
              "candidateRole": "primary",
              "textMetrics": []
            }
          ]
        }
        """;

        var report = DimensionMetricComparator.Compare(source, native);

        Assert.Contains("native-candidate-id-missing", report.GlobalBlockers);
        Assert.False(Assert.Single(report.Candidates).MeasurementsAreUsable);
    }

    [Fact]
    public void Comparator_rejects_unknown_native_metrics_schema()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var native = $"""
        {
          "schemaVersion": "999",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "100", 7.5)}}]
        }
        """;

        var report = DimensionMetricComparator.Compare(source, native);

        Assert.Contains("unsupported-native-metrics-schema", report.GlobalBlockers);
        Assert.False(Assert.Single(report.Candidates).MeasurementsAreUsable);
    }

    [Fact]
    public void Comparator_fails_closed_on_duplicate_native_candidate_identity()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var oneNative = NativeDimension("candidate-1", "100", 7.5);
        var native = $$"""
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{oneNative}}, {{oneNative}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
        Assert.Contains("native-candidate-count-not-one", candidate.Blockers);
    }

    [Fact]
    public void Comparator_rejects_dbtext_axis_aligned_extents_as_width_proof()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var dbTextNative = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"entityType\": \"MText\"",
                "\"entityType\": \"DBText\"",
                StringComparison.Ordinal)
            .Replace(
                "mtext-actual-bounds-dimblock-mcs",
                "dbtext-geometric-extents-dimblock-mcs",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{dbTextNative}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains("native-metric-kind-not-supported", candidate.Blockers);
    }

    [Fact]
    public async Task Cli_command_writes_fail_closed_metric_comparison_report()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "conversion.json");
            var nativePath = Path.Combine(directory, "autocad-metrics.json");
            var outputPath = Path.Combine(directory, "comparison.json");
            await File.WriteAllTextAsync(
                sourcePath,
                SourceReport("candidate-1", "100", 7.5));
            await File.WriteAllTextAsync(
                nativePath,
                $"""
                {
                  "schemaVersion": "2",
                  "drawingName": "probe.dwg",
                  "drawingUnits": "Millimeters",
                  "dimensions": [{{NativeDimension("candidate-1", "100", 7.5)}}]
                }
                """);

            var exitCode = await Program.Main(
            [
                "compare-dimension-metrics",
                "--conversion-report", sourcePath,
                "--autocad-metrics", nativePath,
                "--output", outputPath
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));
            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(outputPath));
            var candidate = Assert.Single(
                json.RootElement
                    .GetProperty("candidates")
                    .EnumerateArray());
            Assert.False(
                candidate.GetProperty("sourceToNativeEquivalenceProven")
                    .GetBoolean());
            Assert.Contains(
                candidate.GetProperty("blockers").EnumerateArray(),
                blocker => blocker.GetString()
                    == "rendered-glyph-equivalence-not-yet-authorized");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Comparator_fails_closed_on_visible_width_drift()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var native = $$"""
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "100", 8.0)}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.True(candidate.MeasurementsAreUsable);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
        Assert.True(candidate.VisibleWidthDeltaMm.HasValue);
        Assert.Equal(0.5d, candidate.VisibleWidthDeltaMm.Value, 9);
        Assert.Contains("visible-width-mismatch", candidate.Blockers);
    }

    [Fact]
    public void Comparator_reports_formatted_source_text_outside_first_safe_subset()
    {
        var source = SourceReport("candidate-1", "10 mm", 7.5);
        var native = $"""
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "10 mm", 7.5)}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Contains(
            "source-text-outside-safe-numeric-subset",
            candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_reports_font_encoding_outside_first_safe_subset()
    {
        var source = SourceReport("candidate-1", "100", 7.5)
            .Replace(
                "\"sourceFontEncodingName\": \"WinAnsiEncoding\"",
                "\"sourceFontEncodingName\": \"Identity-H\"",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "100", 7.5)}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Contains("source-font-encoding-not-winansi", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_accepts_matching_source_font_sha_only_as_one_proof_component()
    {
        const string sha = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var source = $$"""
        {
          "pages": [
            {
              "dimensionMetricEvidence": [
                {
                  "candidateId": "candidate-1",
                  "sourceText": "100",
                  "sourceFontName": "Arial",
                  "sourceFontSha256": "{{sha}}",
                  "sourceFontSubtype": "TrueType",
                  "sourceFontEncodingName": "WinAnsiEncoding",
                  "sourceFontHasToUnicode": false,
                  "sourceFontIsSubset": false,
                  "sourceAdvanceWidthMm": 7.5,
                  "sourceVisibleWidthMm": 7.5,
                  "sourceHeightMm": 2.5,
                  "sourceRotationDegrees": 0,
                  "sourceVisualCenterX": 15,
                  "sourceVisualCenterY": 20
                }
              ]
            }
          ]
        }
        """;
        var native = $$"""
        {
          "schemaVersion": "2",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [
            {{NativeDimension("candidate-1", "100", 7.5, sha)}}
          ]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.DoesNotContain(
            "source-font-program-fingerprint-unavailable",
            candidate.Blockers);
        Assert.DoesNotContain(
            "font-program-sha256-mismatch",
            candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
        Assert.Contains(
            "rendered-glyph-equivalence-not-yet-authorized",
            candidate.Blockers);
    }

    private static string SourceReport(
        string candidateId,
        string text,
        double visibleWidth)
        => $$"""
        {
          "pages": [
            {
              "dimensionMetricEvidence": [
                {
                  "candidateId": "{{candidateId}}",
                  "sourceText": "{{text}}",
                  "sourceFontName": "Helvetica",
                  "sourceFontSubtype": "TrueType",
                  "sourceFontEncodingName": "WinAnsiEncoding",
                  "sourceFontHasToUnicode": false,
                  "sourceFontIsSubset": false,
                  "sourceAdvanceWidthMm": {{visibleWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                  "sourceVisibleWidthMm": {{visibleWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                  "sourceHeightMm": 2.5,
                  "sourceRotationDegrees": 0,
                  "sourceVisualCenterX": 15,
                  "sourceVisualCenterY": 20
                }
              ]
            }
          ]
        }
        """;

    private static string NativeDimension(
        string candidateId,
        string text,
        double width,
        string fontSha = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
        => $$"""
        {
          "dimensionHandle": "10",
          "dimensionType": "RotatedDimension",
          "measurement": 100,
          "dimensionText": "{{text}}",
          "dimensionBlockHandle": "20",
          "candidateId": "{{candidateId}}",
          "candidateRole": "primary",
          "textMetrics": [
            {
              "entityType": "MText",
              "entityHandle": "30",
              "text": "{{text}}",
              "metricKind": "mtext-actual-bounds-dimblock-mcs",
              "width": {{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "height": 2.5,
              "rotationRadians": 0,
              "positionX": 15,
              "positionY": 20,
              "positionZ": 0,
              "textStyleName": "TEYPDFCAD_TEXT",
              "fontFile": "arial.ttf",
              "textStyleWidthFactor": 1,
              "fontResolvedPath": "C:\\Windows\\Fonts\\arial.ttf",
              "fontSha256": "{{fontSha}}",
              "nominalTextHeight": 2.5,
              "entityWidthFactor": 1
            }
          ]
        }
        """;
}
