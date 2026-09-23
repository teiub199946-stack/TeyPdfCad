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
                  "sourceVisibleHeightMm": 2.5,
                  "sourceGlyphInkWidthMm": 7.5,
                  "sourceGlyphInkHeightMm": 2.5,
                  "sourceHeightMm": 2.5,
                  "sourceRotationDegrees": 0,
                  "sourceVisualCenterX": 15,
                  "sourceVisualCenterY": 20,
                  "sourceCoordinateFrame": "drawing-wcs-model-mm",
                  "modelOriginX": 0,
                  "modelOriginY": 0,
                  "sourceLineGeometry": [
                    {
                      "role": "dimension-line",
                      "sourceIds": ["dim"],
                      "startX": 0,
                      "startY": 5,
                      "endX": 10,
                      "endY": 5
                    },
                    {
                      "role": "extension-line",
                      "sourceIds": ["ext-1"],
                      "startX": 0,
                      "startY": 0,
                      "endX": 0,
                      "endY": 6.25
                    },
                    {
                      "role": "extension-line",
                      "sourceIds": ["ext-2"],
                      "startX": 10,
                      "startY": 0,
                      "endX": 10,
                      "endY": 6.25
                    },
                    {
                      "role": "arrow-geometry",
                      "sourceIds": ["arrow-1"],
                      "startX": -1,
                      "startY": 4,
                      "endX": 1,
                      "endY": 6
                    },
                    {
                      "role": "arrow-geometry",
                      "sourceIds": ["arrow-2"],
                      "startX": 9,
                      "startY": 6,
                      "endX": 11,
                      "endY": 4
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;
        var native = $$"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension(
              "p1:dimension:abc",
              "100",
              7.5)}}]
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
          "schemaVersion": "7",
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
    public void Comparator_rejects_missing_regenerated_block_geometry()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                ",\n          \"blockGeometry\": [\n            {\n              \"entityType\": \"Line\",\n              \"entityHandle\": \"40\",\n              \"geometryKind\": \"line\",\n              \"startX\": 0,\n              \"startY\": 5,\n              \"startZ\": 0,\n              \"endX\": 10,\n              \"endY\": 5,\n              \"endZ\": 0,\n              \"minX\": 0,\n              \"minY\": 5,\n              \"minZ\": 0,\n              \"maxX\": 10,\n              \"maxY\": 5,\n              \"maxZ\": 0,\n              \"nestedBlockName\": \"\",\n              \"vertexCount\": 0\n            }\n          ]",
                string.Empty,
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains("native-block-geometry-missing", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_reports_exact_source_extension_line_matches_in_exploded_geometry()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "100", 7.5)}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Equal(2, candidate.SourceExtensionLineCount);
        Assert.Equal(2, candidate.MatchedSourceExtensionLineCount);
        Assert.DoesNotContain("source-extension-line-unmatched", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_fails_closed_when_source_extension_line_has_no_exploded_match()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"endY\": 6.25",
                "\"endY\": 6.5",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.True(candidate.MatchedSourceExtensionLineCount < candidate.SourceExtensionLineCount);
        Assert.Contains("source-extension-line-unmatched", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_reports_exact_source_tick_matches_in_exploded_geometry()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "100", 7.5)}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Equal(2, candidate.SourceArrowLineCount);
        Assert.Equal(2, candidate.MatchedSourceArrowLineCount);
        Assert.DoesNotContain("source-arrow-line-unmatched", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_fails_closed_when_exploded_tick_length_drifts()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"startX\": -1,\n              \"startY\": 4,\n              \"startZ\": 0,\n              \"endX\": 1,\n              \"endY\": 6",
                "\"startX\": -1.2,\n              \"startY\": 3.8,\n              \"startZ\": 0,\n              \"endX\": 1.2,\n              \"endY\": 6.2",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.True(candidate.MatchedSourceArrowLineCount < candidate.SourceArrowLineCount);
        Assert.Contains("source-arrow-line-unmatched", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_rejects_unknown_exploded_geometry_coordinate_frame()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"explodedGeometryCoordinateFrame\": \"drawing-wcs\"",
                "\"explodedGeometryCoordinateFrame\": \"dimension-block-mcs\"",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains(
            "native-exploded-coordinate-frame-unsupported",
            candidate.Blockers);
    }

    [Fact]
    public void Comparator_rejects_missing_exploded_dimension_geometry()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var exploded = """
,
          "explodedGeometry": [
            {
              "entityType": "Line",
              "entityHandle": "explode-0",
              "geometryKind": "line",
              "startX": 0,
              "startY": 5,
              "startZ": 0,
              "endX": 10,
              "endY": 5,
              "endZ": 0,
              "minX": 0,
              "minY": 5,
              "minZ": 0,
              "maxX": 10,
              "maxY": 5,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-1",
              "geometryKind": "line",
              "startX": 0,
              "startY": 0,
              "startZ": 0,
              "endX": 0,
              "endY": 6.25,
              "endZ": 0,
              "minX": 0,
              "minY": 0,
              "minZ": 0,
              "maxX": 0,
              "maxY": 6.25,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-2",
              "geometryKind": "line",
              "startX": 10,
              "startY": 0,
              "startZ": 0,
              "endX": 10,
              "endY": 6.25,
              "endZ": 0,
              "minX": 10,
              "minY": 0,
              "minZ": 0,
              "maxX": 10,
              "maxY": 6.25,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-3",
              "geometryKind": "line",
              "startX": -1,
              "startY": 4,
              "startZ": 0,
              "endX": 1,
              "endY": 6,
              "endZ": 0,
              "minX": -1,
              "minY": 4,
              "minZ": 0,
              "maxX": 1,
              "maxY": 6,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-4",
              "geometryKind": "line",
              "startX": 9,
              "startY": 6,
              "startZ": 0,
              "endX": 11,
              "endY": 4,
              "endZ": 0,
              "minX": 9,
              "minY": 4,
              "minZ": 0,
              "maxX": 11,
              "maxY": 6,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            }
          ]
""";
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(exploded, string.Empty, StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains("native-exploded-geometry-missing", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_rejects_legacy_schema_without_fragment_evidence()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var native = $"""
        {
          "schemaVersion": "2",
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
          "schemaVersion": "7",
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
          "schemaVersion": "7",
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
                  "schemaVersion": "7",
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
          "schemaVersion": "7",
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
    public void Comparator_detects_fragment_width_drift_even_when_mtext_actual_width_matches()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"extentWidth\": 7.5",
                "\"extentWidth\": 7.7",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Contains("native-fragment-width-mismatch", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_detects_source_glyph_ink_width_drift()
    {
        var source = SourceReport("candidate-1", "100", 7.5)
            .Replace(
                "\"sourceGlyphInkWidthMm\": 7.5",
                "\"sourceGlyphInkWidthMm\": 7.2",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{NativeDimension("candidate-1", "100", 7.5)}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.True(candidate.GlyphInkWidthDeltaMm.HasValue);
        Assert.Equal(0.3d, candidate.GlyphInkWidthDeltaMm.Value, 9);
        Assert.Contains("glyph-ink-width-mismatch", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_detects_projected_height_drift_against_native_fragment()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"extentHeight\": 2.5",
                "\"extentHeight\": 3.0",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Contains("projected-height-mismatch", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_reports_formatted_source_text_outside_first_safe_subset()
    {
        var source = SourceReport("candidate-1", "10 mm", 7.5);
        var native = $"""
        {
          "schemaVersion": "7",
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
          "schemaVersion": "7",
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
    public void Comparator_rejects_explicit_native_text_override_in_first_safe_subset()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"dimensionText\": \"\"",
                "\"dimensionText\": \"100\"",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.Contains("native-text-override-present", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_rejects_regenerated_mtext_background_mask_or_border()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"backgroundFill\": false",
                "\"backgroundFill\": true",
                StringComparison.Ordinal)
            .Replace(
                "\"showBorders\": false",
                "\"showBorders\": true",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains("native-mtext-background-fill-present", candidate.Blockers);
        Assert.Contains("native-mtext-border-present", candidate.Blockers);
        Assert.False(candidate.SourceToNativeEquivalenceProven);
    }

    [Fact]
    public void Comparator_rejects_fragment_width_factor_override()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var nativeDimension = NativeDimension("candidate-1", "100", 7.5)
            .Replace(
                "\"widthFactor\": 1",
                "\"widthFactor\": 0.8",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{nativeDimension}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains("native-fragment-width-factor-not-one", candidate.Blockers);
    }

    [Fact]
    public void Comparator_rejects_multiple_or_formatted_native_fragments()
    {
        var source = SourceReport("candidate-1", "100", 7.5);
        var one = NativeDimension("candidate-1", "100", 7.5);
        var formatted = one
            .Replace(
                "\"trackingFactor\": 1",
                "\"trackingFactor\": 1.1",
                StringComparison.Ordinal)
            .Replace(
                "\"underlined\": false",
                "\"underlined\": true",
                StringComparison.Ordinal);
        var native = $"""
        {
          "schemaVersion": "7",
          "drawingName": "probe.dwg",
          "drawingUnits": "Millimeters",
          "dimensions": [{{formatted}}]
        }
        """;

        var candidate = Assert.Single(
            DimensionMetricComparator.Compare(source, native).Candidates);

        Assert.False(candidate.MeasurementsAreUsable);
        Assert.Contains("native-fragment-tracking-not-one", candidate.Blockers);
        Assert.Contains("native-fragment-formatting-not-plain", candidate.Blockers);
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
                  "sourceVisibleHeightMm": 2.5,
                  "sourceGlyphInkWidthMm": 7.5,
                  "sourceGlyphInkHeightMm": 2.5,
                  "sourceHeightMm": 2.5,
                  "sourceRotationDegrees": 0,
                  "sourceVisualCenterX": 15,
                  "sourceVisualCenterY": 20,
                  "sourceCoordinateFrame": "drawing-wcs-model-mm",
                  "modelOriginX": 0,
                  "modelOriginY": 0,
                  "sourceLineGeometry": [
                    {
                      "role": "dimension-line",
                      "sourceIds": ["dim"],
                      "startX": 0,
                      "startY": 5,
                      "endX": 10,
                      "endY": 5
                    },
                    {
                      "role": "extension-line",
                      "sourceIds": ["ext-1"],
                      "startX": 0,
                      "startY": 0,
                      "endX": 0,
                      "endY": 6.25
                    },
                    {
                      "role": "extension-line",
                      "sourceIds": ["ext-2"],
                      "startX": 10,
                      "startY": 0,
                      "endX": 10,
                      "endY": 6.25
                    },
                    {
                      "role": "arrow-geometry",
                      "sourceIds": ["arrow-1"],
                      "startX": -1,
                      "startY": 4,
                      "endX": 1,
                      "endY": 6
                    },
                    {
                      "role": "arrow-geometry",
                      "sourceIds": ["arrow-2"],
                      "startX": 9,
                      "startY": 6,
                      "endX": 11,
                      "endY": 4
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;
        var native = $$"""
        {
          "schemaVersion": "7",
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
                  "sourceVisibleHeightMm": 2.5,
                  "sourceGlyphInkWidthMm": {{visibleWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                  "sourceGlyphInkHeightMm": 2.5,
                  "sourceHeightMm": 2.5,
                  "sourceRotationDegrees": 0,
                  "sourceVisualCenterX": 15,
                  "sourceVisualCenterY": 20,
                  "sourceCoordinateFrame": "drawing-wcs-model-mm",
                  "modelOriginX": 0,
                  "modelOriginY": 0,
                  "sourceLineGeometry": [
                    {
                      "role": "dimension-line",
                      "sourceIds": ["dim"],
                      "startX": 0,
                      "startY": 5,
                      "endX": 10,
                      "endY": 5
                    },
                    {
                      "role": "extension-line",
                      "sourceIds": ["ext-1"],
                      "startX": 0,
                      "startY": 0,
                      "endX": 0,
                      "endY": 6.25
                    },
                    {
                      "role": "extension-line",
                      "sourceIds": ["ext-2"],
                      "startX": 10,
                      "startY": 0,
                      "endX": 10,
                      "endY": 6.25
                    },
                    {
                      "role": "arrow-geometry",
                      "sourceIds": ["arrow-1"],
                      "startX": -1,
                      "startY": 4,
                      "endX": 1,
                      "endY": 6
                    },
                    {
                      "role": "arrow-geometry",
                      "sourceIds": ["arrow-2"],
                      "startX": 9,
                      "startY": 6,
                      "endX": 11,
                      "endY": 4
                    }
                  ]
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
          "dimensionText": "",
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
              "entityWidthFactor": 1,
              "backgroundFill": false,
              "useBackgroundColor": false,
              "backgroundScaleFactor": 0,
              "showBorders": false,
              "attachment": "MiddleCenter",
              "fragments": [
                {
                  "text": "{{text}}",
                  "trueTypeFont": "Arial",
                  "shxFont": "",
                  "extentWidth": {{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                  "extentHeight": 2.5,
                  "capsHeight": 2.5,
                  "trackingFactor": 1,
                  "widthFactor": 1,
                  "obliqueAngle": 0,
                  "locationX": 15,
                  "locationY": 20,
                  "locationZ": 0,
                  "directionX": 1,
                  "directionY": 0,
                  "directionZ": 0,
                  "bold": false,
                  "italic": false,
                  "stackTop": false,
                  "stackBottom": false,
                  "underlined": false,
                  "overlined": false,
                  "strikethrough": false
                }
              ]
            }
          ],
          "blockGeometry": [
            {
              "entityType": "Line",
              "entityHandle": "40",
              "geometryKind": "line",
              "startX": 0,
              "startY": 5,
              "startZ": 0,
              "endX": 10,
              "endY": 5,
              "endZ": 0,
              "minX": 0,
              "minY": 5,
              "minZ": 0,
              "maxX": 10,
              "maxY": 5,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            }
          ],
          "explodedGeometry": [
            {
              "entityType": "Line",
              "entityHandle": "explode-0",
              "geometryKind": "line",
              "startX": 0,
              "startY": 5,
              "startZ": 0,
              "endX": 10,
              "endY": 5,
              "endZ": 0,
              "minX": 0,
              "minY": 5,
              "minZ": 0,
              "maxX": 10,
              "maxY": 5,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-1",
              "geometryKind": "line",
              "startX": 0,
              "startY": 0,
              "startZ": 0,
              "endX": 0,
              "endY": 6.25,
              "endZ": 0,
              "minX": 0,
              "minY": 0,
              "minZ": 0,
              "maxX": 0,
              "maxY": 6.25,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-2",
              "geometryKind": "line",
              "startX": 10,
              "startY": 0,
              "startZ": 0,
              "endX": 10,
              "endY": 6.25,
              "endZ": 0,
              "minX": 10,
              "minY": 0,
              "minZ": 0,
              "maxX": 10,
              "maxY": 6.25,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-3",
              "geometryKind": "line",
              "startX": -1,
              "startY": 4,
              "startZ": 0,
              "endX": 1,
              "endY": 6,
              "endZ": 0,
              "minX": -1,
              "minY": 4,
              "minZ": 0,
              "maxX": 1,
              "maxY": 6,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            },
            {
              "entityType": "Line",
              "entityHandle": "explode-4",
              "geometryKind": "line",
              "startX": 9,
              "startY": 6,
              "startZ": 0,
              "endX": 11,
              "endY": 4,
              "endZ": 0,
              "minX": 9,
              "minY": 4,
              "minZ": 0,
              "maxX": 11,
              "maxY": 6,
              "maxZ": 0,
              "nestedBlockName": "",
              "vertexCount": 0
            }
          ],
          "blockGeometryCoordinateFrame": "dimension-block-mcs",
          "explodedGeometryCoordinateFrame": "drawing-wcs"
        }
        """;
}
