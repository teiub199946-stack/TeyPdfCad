using System.Text;
using System.Text.Json;
using ACadSharp.IO;
using TeyPdfCad.Cli;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Templates;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Cli.Tests;

public sealed class ConversionPipelineTests
{
    [Fact]
    public async Task Pipeline_writes_one_dwg_and_complete_json_report_for_a_vector_pdf()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateMinimalPdf("0 0 m 10 10 l S"));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.Complete, result.Outcome);
        Assert.True(File.Exists(output));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("pagesProcessed").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("layoutsReadBack").GetInt32());
    }

    [Fact]
    public async Task Pipeline_rejects_a_pdf_without_vector_entities_without_creating_a_dwg()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "empty.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateMinimalPdf(string.Empty));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.UnsupportedVectorContent, result.Outcome);
        Assert.False(File.Exists(output));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task Pipeline_reports_a_malformed_pdf_instead_of_crashing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "broken.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllTextAsync(input, "not a PDF");

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.InvalidArgumentsOrIo, result.Outcome);
        Assert.False(File.Exists(output));
        Assert.True(File.Exists(report));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task Pipeline_never_overwrites_the_input_pdf_with_the_dwg()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.pdf");
        var report = Path.Combine(directory, "result.json");
        var original = CreateMinimalPdf("0 0 m 10 10 l S");
        await File.WriteAllBytesAsync(input, original);

        var result = await new ConversionPipeline().ConvertAsync(input, input, report, default);

        Assert.Equal(ConversionOutcome.InvalidArgumentsOrIo, result.Outcome);
        Assert.Equal(original, await File.ReadAllBytesAsync(input));
    }

    [Fact]
    public async Task Pipeline_rejects_a_missing_template_manifest_without_writing_dwg()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateMinimalPdf("0 0 m 10 10 l S"));

        var result = await new ConversionPipeline().ConvertAsync(
            input, output, report, default, Path.Combine(directory, "missing.json"));

        Assert.Equal(ConversionOutcome.InvalidArgumentsOrIo, result.Outcome);
        Assert.False(File.Exists(output));
        Assert.True(File.Exists(report));
    }

    [Fact]
    public async Task Pipeline_reports_why_a_template_was_not_selected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        var manifest = Path.Combine(directory, "templates.json");
        await File.WriteAllBytesAsync(input, CreateMinimalPdf("0 0 m 10 10 l S"));
        await File.WriteAllTextAsync(manifest, """
        { "schemaVersion":"1", "blocks":[{
          "name":"A3-landscape",
          "entities":[{"objectClass":"AcDbLine","handle":"A1","points":[{"x":0,"y":0},{"x":420,"y":0}]}],
          "attributes":[]
        }] }
        """);

        var result = await new ConversionPipeline().ConvertAsync(
            input, output, report, default, manifest);

        Assert.Equal(ConversionOutcome.Complete, result.Outcome);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.False(page.GetProperty("templateSelected").GetBoolean());
        Assert.Equal("title-block-not-confirmed", page.GetProperty("templateReason").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("templateName").ValueKind);
    }

    [Fact]
    public async Task Pipeline_reports_source_text_metric_evidence_keyed_by_candidate_id()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "dimension-metrics.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        const string contents =
            "20 50 m 120 50 l S " +
            "20 38 m 20 51 l S " +
            "120 38 m 120 51 l S " +
            "19 49 m 21 51 l S " +
            "119 49 m 121 51 l S " +
            "BT /F1 12 Tf 60 53 Td (35.28) Tj ET";
        await File.WriteAllBytesAsync(
            input,
            CreateMinimalPdfWithHelvetica(contents, 160, 100));

        _ = await new ConversionPipeline().ConvertAsync(
            input,
            output,
            report,
            default);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.True(page.GetProperty("dimensionCandidateCount").GetInt32() > 0);
        var evidence = Assert.Single(
            page.GetProperty("dimensionMetricEvidence").EnumerateArray());

        Assert.False(string.IsNullOrWhiteSpace(
            evidence.GetProperty("candidateId").GetString()));
        Assert.Equal(
            "Helvetica",
            evidence.GetProperty("sourceFontName").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("sourceFontSha256").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("sourceFontSubtype").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("sourceFontEncodingName").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("sourceFontHasToUnicode").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("sourceFontIsSubset").ValueKind);
        Assert.True(
            evidence.GetProperty("sourceAdvanceWidthMm").GetDouble() > 0d);
        Assert.True(
            evidence.GetProperty("sourceVisibleWidthMm").GetDouble() > 0d);
        Assert.True(
            evidence.GetProperty("sourceHeightMm").GetDouble() > 0d);
        Assert.Equal(
            "drawing-wcs-model-mm",
            evidence.GetProperty("sourceCoordinateFrame").GetString());
        Assert.Equal(0d, evidence.GetProperty("modelOriginX").GetDouble(), 9);
        Assert.Equal(0d, evidence.GetProperty("modelOriginY").GetDouble(), 9);

        var sourceGeometry = evidence
            .GetProperty("sourceLineGeometry")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(5, sourceGeometry.Length);
        Assert.Single(sourceGeometry.Where(item =>
            item.GetProperty("role").GetString() == "dimension-line"));
        Assert.Equal(2, sourceGeometry.Count(item =>
            item.GetProperty("role").GetString() == "extension-line"));
        Assert.Equal(2, sourceGeometry.Count(item =>
            item.GetProperty("role").GetString() == "arrow-geometry"));
        Assert.All(sourceGeometry, item =>
        {
            Assert.True(double.IsFinite(item.GetProperty("startX").GetDouble()));
            Assert.True(double.IsFinite(item.GetProperty("startY").GetDouble()));
            Assert.True(double.IsFinite(item.GetProperty("endX").GetDouble()));
            Assert.True(double.IsFinite(item.GetProperty("endY").GetDouble()));
        });
    }

    [Fact]
    public async Task Optional_probe_output_preserves_sources_and_exposes_native_dimension_candidate()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "dimension-probe.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        var probe = Path.Combine(directory, "result.probe.dwg");
        const string contents =
            "20 50 m 120 50 l S " +
            "20 38 m 20 51 l S " +
            "120 38 m 120 51 l S " +
            "19 49 m 21 51 l S " +
            "119 49 m 121 51 l S " +
            "BT /F1 12 Tf 60 53 Td (35.28) Tj ET";
        await File.WriteAllBytesAsync(
            input,
            CreateMinimalPdfWithHelvetica(contents, 160, 100));

        var result = await new ConversionPipeline().ConvertAsync(
            input,
            output,
            report,
            default,
            diagnosticProbeOutputPath: probe);

        Assert.True(File.Exists(output));
        Assert.True(File.Exists(probe));
        Assert.Equal(Path.GetFullPath(probe), result.ProbeDwgPath);

        var published = DwgReader.Read(output);
        Assert.Empty(published.Entities.OfType<ACadSharp.Entities.Dimension>());

        var diagnostic = DwgReader.Read(probe);
        var native = Assert.Single(diagnostic.Entities.OfType<ACadSharp.Entities.Dimension>());
        Assert.True(CandidateMetadataCodec.TryRead(native, out var metadata));
        Assert.False(string.IsNullOrWhiteSpace(metadata.CandidateId));
        Assert.Equal("primary", metadata.Role);
        Assert.NotEmpty(diagnostic.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.NotEmpty(diagnostic.Entities.OfType<ACadSharp.Entities.TextEntity>());
    }

    [Fact]
    public async Task Pipeline_preserves_uncertain_pattern_geometry_without_native_hatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "hatch.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        const string content = "0 0 20 20 re S 1 4 m 19 4 l S 1 8 m 19 8 l S 1 12 m 19 12 l S";
        await File.WriteAllBytesAsync(input, CreateMinimalPdf(content));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Equal(0, page.GetProperty("patternHatchCount").GetInt32());
        Assert.Equal("PASS_WITH_RESIDUALS", page.GetProperty("semanticAuditStatus").GetString());
        Assert.Contains("hatch-uncertain", page.GetProperty("recognitionWarnings").EnumerateArray()
            .Select(value => value.GetString()));
        var drawing = ACadSharp.IO.DwgReader.Read(output);
        Assert.DoesNotContain(drawing.Entities.OfType<ACadSharp.Entities.Hatch>(), hatch => !hatch.IsSolid);
        Assert.Equal(3, drawing.Entities.OfType<ACadSharp.Entities.Line>().Count());
    }


    [Fact]
    public async Task Pipeline_preserves_axis_sources_while_source_equivalence_is_incomplete()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "axis.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");

        // Three collinear segments satisfy the segmented-axis recognizer:
        // two long segments + one short segment, with small deterministic gaps.
        const string content =
            "10 40 m 80 40 l S " +
            "90 40 m 160 40 l S " +
            "170 40 m 180 40 l S";
        await File.WriteAllBytesAsync(
            input,
            CreateMinimalPdf(content, 240, 100));

        var result = await new ConversionPipeline().ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        Assert.True(File.Exists(output));

        var drawing = ACadSharp.IO.DwgReader.Read(output);
        Assert.Equal(3, drawing.Entities.OfType<ACadSharp.Entities.Line>().Count());
        Assert.Empty(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        Assert.DoesNotContain(
            drawing.Entities,
            entity => CandidateMetadataCodec.TryRead(entity, out _));

        using var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(report));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var page = Assert.Single(
            json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Equal(1, page.GetProperty("axisCandidateCount").GetInt32());
        Assert.Equal(0, page.GetProperty("suppressedSourceCount").GetInt32());
        Assert.Equal("PASS_WITH_RESIDUALS", page.GetProperty("semanticAuditStatus").GetString());
        Assert.False(page.GetProperty("complete").GetBoolean());
        Assert.Contains(
            page.GetProperty("replacementResiduals").EnumerateArray(),
            residual => residual.GetProperty("kind").GetInt32()
                == (int)ReplacementResidualKind.SourceEquivalenceIncomplete);
    }


    [Fact]
    public async Task Pipeline_keeps_sources_when_probe_verification_rejects_a_native_candidate()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "axis.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateSegmentedAxisPdf());

        var pipeline = new ConversionPipeline(
            new ProductionDwgDocumentWriter(),
            new RejectFirstCandidateVerification());

        var result = await pipeline.ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        Assert.True(File.Exists(output));

        var drawing = DwgReader.Read(output);
        Assert.Equal(3, drawing.Entities.OfType<ACadSharp.Entities.Line>().Count());
        Assert.DoesNotContain(
            drawing.Entities.OfType<ACadSharp.Entities.Insert>(),
            insert => insert.Block.Name == "TEY_AXIS");

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Equal(0, page.GetProperty("suppressedSourceCount").GetInt32());
        Assert.Equal("PASS_WITH_RESIDUALS", page.GetProperty("semanticAuditStatus").GetString());
        Assert.Contains(
            page.GetProperty("replacementResiduals").EnumerateArray(),
            residual => residual.GetProperty("kind").GetInt32()
                == (int)ReplacementResidualKind.CandidateNotVerified);
    }

    [Fact]
    public async Task Pipeline_does_not_publish_dwg_when_final_native_fingerprint_changes()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "axis.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateSegmentedAxisPdf());

        var pipeline = new ConversionPipeline(
            new MutateFinalAxisWriter(),
            new ForceEquivalenceCompleteVerifier());

        var result = await pipeline.ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.InvalidArgumentsOrIo, result.Outcome);
        Assert.False(File.Exists(output));
        AssertNoPipelineTempDwgs(directory);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.Contains(
            json.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains(
                "SourceSuppressionViolation",
                StringComparison.Ordinal));
        Assert.Contains(
            json.RootElement.GetProperty("fatalResiduals").EnumerateArray(),
            residual => residual.GetProperty("kind").GetInt32()
                == (int)ReplacementResidualKind.SourceSuppressionViolation
                && residual.GetProperty("severity").GetInt32()
                == (int)ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public async Task Pipeline_does_not_publish_dwg_when_requested_source_fingerprint_is_absent_from_probe()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "axis.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateSegmentedAxisPdf());

        var pipeline = new ConversionPipeline(
            new CorruptProbeSourceEmissionWriter(),
            new ForceEquivalenceCompleteVerifier());

        var result = await pipeline.ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.InvalidArgumentsOrIo, result.Outcome);
        Assert.False(File.Exists(output));
        AssertNoPipelineTempDwgs(directory);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.Contains(
            json.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains(
                "SourceSuppressionViolation",
                StringComparison.Ordinal));
        Assert.Contains(
            json.RootElement.GetProperty("fatalResiduals").EnumerateArray(),
            residual => residual.GetProperty("kind").GetInt32()
                == (int)ReplacementResidualKind.SourceSuppressionViolation
                && residual.GetProperty("severity").GetInt32()
                == (int)ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public async Task Pipeline_does_not_publish_dwg_when_wrong_source_is_authorized()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "axis.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateSegmentedAxisPdf());

        var pipeline = new ConversionPipeline(
            new AddUnauthorizedSourceOnFinalWriter(),
            new ForceEquivalenceCompleteVerifier());

        var result = await pipeline.ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.InvalidArgumentsOrIo, result.Outcome);
        Assert.False(File.Exists(output));
        AssertNoPipelineTempDwgs(directory);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.Contains(
            json.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains(
                "SourceSuppressionViolation",
                StringComparison.Ordinal));
        Assert.Contains(
            json.RootElement.GetProperty("fatalResiduals").EnumerateArray(),
            residual => residual.GetProperty("kind").GetInt32()
                == (int)ReplacementResidualKind.SourceSuppressionViolation
                && residual.GetProperty("severity").GetInt32()
                == (int)ReplacementResidualSeverity.Critical);
    }

    [Fact]
    public async Task Pipeline_marks_the_result_partial_when_a_pdf_path_operation_is_unsupported()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "clipped.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateMinimalPdf("0 0 m 20 0 l 10 10 l 20 20 l 0 20 l h W n 1 1 m 10 10 l S"));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        Assert.True(File.Exists(output));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.False(page.GetProperty("complete").GetBoolean());
        Assert.Contains(page.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "unsupported-pdf-path-operation");
    }

    [Fact]
    public async Task Pipeline_accepts_a_full_page_clipping_rectangle_as_lossless()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "page-clipped.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateMinimalPdf("0 0 72 72 re W n 1 1 m 10 10 l S"));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.Complete, result.Outcome);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Empty(page.GetProperty("diagnostics").EnumerateArray());
    }


    [Fact]
    public async Task Pipeline_reports_unsafe_page_independently_in_two_page_document()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "two-page.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");

        const string uncertainHatchPage =
            "0 0 60 60 re S " +
            "5 10 m 55 10 l S " +
            "5 20 m 55 20 l S " +
            "5 30 m 55 30 l S";
        const string ordinaryPage = "5 5 m 25 20 l S";
        await File.WriteAllBytesAsync(
            input,
            CreateTwoPagePdf(
                uncertainHatchPage,
                ordinaryPage,
                72,
                72));

        var result = await new ConversionPipeline().ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        Assert.True(File.Exists(output));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var pages = json.RootElement.GetProperty("pages").EnumerateArray().ToArray();
        Assert.Equal(2, pages.Length);

        Assert.Equal(1, pages[0].GetProperty("pageNumber").GetInt32());
        Assert.Equal(
            "PASS_WITH_RESIDUALS",
            pages[0].GetProperty("semanticAuditStatus").GetString());
        Assert.False(pages[0].GetProperty("complete").GetBoolean());
        Assert.Contains(
            pages[0].GetProperty("recognitionWarnings").EnumerateArray(),
            warning => warning.GetString() is "hatch-uncertain" or "hatch-low-confidence");

        Assert.Equal(2, pages[1].GetProperty("pageNumber").GetInt32());
        Assert.Equal(
            "FULL_PASS",
            pages[1].GetProperty("semanticAuditStatus").GetString());
        Assert.True(pages[1].GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task Pipeline_skips_advisory_semantics_when_page_complexity_is_too_high()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "complex.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        var content = string.Join(' ', Enumerable.Range(0, 2_001).Select(index => $"0 {index % 72} m 72 {index % 72} l S"));
        await File.WriteAllBytesAsync(input, CreateMinimalPdf(content));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Equal("PASS_WITH_RESIDUALS", page.GetProperty("semanticAuditStatus").GetString());
        Assert.Contains("semantic-recognition-skipped-complexity", page.GetProperty("semanticWarnings").EnumerateArray().Select(value => value.GetString()));
    }


    [Fact]
    public async Task Production_interlock_withholds_probe_only_native_axis_from_published_dwg()
    {
        var directory = CreateTestDirectory();
        var input = Path.Combine(directory, "axis.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        await File.WriteAllBytesAsync(input, CreateSegmentedAxisPdf());

        var result = await new ConversionPipeline().ConvertAsync(
            input,
            output,
            report,
            default);

        Assert.Equal(ConversionOutcome.Partial, result.Outcome);
        Assert.True(File.Exists(output));

        var drawing = DwgReader.Read(output);
        Assert.NotEmpty(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Empty(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        Assert.DoesNotContain(
            drawing.Entities,
            entity => CandidateMetadataCodec.TryRead(entity, out _));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        Assert.Contains(
            json.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains(
                "SourceEquivalenceIncomplete",
                StringComparison.Ordinal));
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static byte[] CreateSegmentedAxisPdf()
        => CreateMinimalPdf(
            "10 40 m 80 40 l S " +
            "90 40 m 160 40 l S " +
            "170 40 m 180 40 l S",
            240,
            100);

    private static void AssertNoPipelineTempDwgs(string directory)
        => Assert.Empty(
            Directory.EnumerateFiles(
                directory,
                ".teypdfcad-*.dwg",
                SearchOption.TopDirectoryOnly));

    private sealed class ForceEquivalenceCompleteVerifier : IDwgReadBackVerifier
    {
        private readonly DwgReadBackVerifier _inner = new();

        public NativeReadBackVerification Verify(
            string dwgPath,
            NativeWriteManifest manifest)
        {
            var actual = _inner.Verify(dwgPath, manifest);
            return new NativeReadBackVerification(
                actual.Candidates.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        SourceEquivalenceComplete = true
                    },
                    StringComparer.Ordinal));
        }

        public DwgStructuralInventory ReadStructuralInventory(string dwgPath)
            => _inner.ReadStructuralInventory(dwgPath);
    }

    private sealed class RejectFirstCandidateVerification : IDwgReadBackVerifier
    {
        private readonly DwgReadBackVerifier _inner = new();
        private int _verifyCount;

        public NativeReadBackVerification Verify(
            string dwgPath,
            NativeWriteManifest manifest)
        {
            var actual = _inner.Verify(dwgPath, manifest);
            _verifyCount++;
            if (_verifyCount != 1)
                return actual;

            return new NativeReadBackVerification(
                actual.Candidates.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        IsVerified = false,
                        SourceEquivalenceComplete = true,
                        InvalidEntities =
                        [
                            ..pair.Value.InvalidEntities,
                            "Injected probe rejection."
                        ]
                    },
                    StringComparer.Ordinal));
        }

        public DwgStructuralInventory ReadStructuralInventory(string dwgPath)
            => _inner.ReadStructuralInventory(dwgPath);
    }

    private sealed class MutateFinalAxisWriter : IDwgDocumentWriter
    {
        private readonly AcadSharpDwgWriter _inner = new();

        public DwgWriteResult Write(
            Stream destination,
            VectorPdfDocument source,
            DwgDocumentPlan plan,
            IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
            IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
            TemplateLibrary? templateLibrary = null,
            IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
            IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
            IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null)
        {
            if (authorizedSuppressedSources is null)
            {
                return _inner.Write(
                    destination,
                    source,
                    plan,
                    hatchRecognitionByPage,
                    semanticRecognitionByPage,
                    templateLibrary,
                    templateSelectionsByPage,
                    sourceReplacementPlansByPage,
                    null);
            }

            using var buffer = new MemoryStream();
            var result = _inner.Write(
                buffer,
                source,
                plan,
                hatchRecognitionByPage,
                semanticRecognitionByPage,
                templateLibrary,
                templateSelectionsByPage,
                sourceReplacementPlansByPage,
                authorizedSuppressedSources);
            buffer.Position = 0;
            var drawing = DwgReader.Read(buffer);
            var axis = Assert.Single(
                drawing.Entities.OfType<ACadSharp.Entities.Insert>(),
                insert => insert.Block.Name == "TEY_AXIS");
            axis.XScale *= 1.125d;

            var writer = new DwgWriter(destination, drawing)
            {
                Configuration = new DwgWriterConfiguration
                {
                    CloseStream = false
                }
            };
            writer.Write();
            return result;
        }
    }

    private sealed class CorruptProbeSourceEmissionWriter : IDwgDocumentWriter
    {
        private readonly AcadSharpDwgWriter _inner = new();

        public DwgWriteResult Write(
            Stream destination,
            VectorPdfDocument source,
            DwgDocumentPlan plan,
            IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
            IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
            TemplateLibrary? templateLibrary = null,
            IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
            IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
            IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null)
        {
            var result = _inner.Write(
                destination,
                source,
                plan,
                hatchRecognitionByPage,
                semanticRecognitionByPage,
                templateLibrary,
                templateSelectionsByPage,
                sourceReplacementPlansByPage,
                authorizedSuppressedSources);

            if (authorizedSuppressedSources is not null)
                return result;

            var corrupted = result.SourceEmissionSummary.OutputFingerprintCountsBySource
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, int>)new Dictionary<string, int>(
                        StringComparer.Ordinal)
                    {
                        [$"absent-probe-fingerprint:{pair.Key}"] = pair.Value.Values.Sum()
                    });

            return result with
            {
                SourceEmissionSummary = new SourceEmissionSummary(corrupted)
            };
        }
    }

    private sealed class AddUnauthorizedSourceOnFinalWriter : IDwgDocumentWriter
    {
        private readonly AcadSharpDwgWriter _inner = new();

        public DwgWriteResult Write(
            Stream destination,
            VectorPdfDocument source,
            DwgDocumentPlan plan,
            IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
            IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
            TemplateLibrary? templateLibrary = null,
            IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
            IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
            IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null)
        {
            var authorization = authorizedSuppressedSources;
            if (authorization is not null)
            {
                authorization = authorization
                    .Append(new PageSourceRef(1, "not-eligible-source"))
                    .ToHashSet();
            }

            return _inner.Write(
                destination,
                source,
                plan,
                hatchRecognitionByPage,
                semanticRecognitionByPage,
                templateLibrary,
                templateSelectionsByPage,
                sourceReplacementPlansByPage,
                authorization);
        }
    }


    private static byte[] CreateTwoPagePdf(
        string firstContents,
        string secondContents,
        int widthPoints,
        int heightPoints)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints} {heightPoints}] /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(firstContents)} >>\nstream\n{firstContents}\nendstream",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints} {heightPoints}] /Contents 6 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(secondContents)} >>\nstream\n{secondContents}\nendstream"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder
                .Append(index + 1)
                .Append(" 0 obj\n")
                .Append(objects[index])
                .Append("\nendobj\n");
        }

        var startXref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 7\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder
                .Append(offset.ToString("D10"))
                .Append(" 00000 n \n");
        }

        builder
            .Append("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n")
            .Append(startXref)
            .Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateMinimalPdfWithHelvetica(
        string contents,
        int widthPoints,
        int heightPoints)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints} {heightPoints}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contents)} >>\nstream\n{contents}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder
                .Append(index + 1)
                .Append(" 0 obj\n")
                .Append(objects[index])
                .Append("\nendobj\n");
        }

        var startXref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder
            .Append("xref\n0 ")
            .Append(objects.Length + 1)
            .Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder
                .Append(offset.ToString("D10"))
                .Append(" 00000 n \n");
        }

        builder
            .Append("trailer\n<< /Size ")
            .Append(objects.Length + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(startXref)
            .Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateMinimalPdf(
        string contents,
        int widthPoints = 72,
        int heightPoints = 72)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints} {heightPoints}] /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contents)} >>\nstream\n{contents}\nendstream"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var startXref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n").Append(startXref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
