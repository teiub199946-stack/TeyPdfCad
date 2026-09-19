using System.Text;
using System.Text.Json;
using TeyPdfCad.Cli;
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
    public async Task Pipeline_reports_and_writes_a_confirmed_pattern_hatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "hatch.pdf");
        var output = Path.Combine(directory, "result.dwg");
        var report = Path.Combine(directory, "result.json");
        const string content = "0 0 20 20 re S 1 4 m 19 4 l S 1 8 m 19 8 l S 1 12 m 19 12 l S";
        await File.WriteAllBytesAsync(input, CreateMinimalPdf(content));

        var result = await new ConversionPipeline().ConvertAsync(input, output, report, default);

        Assert.Equal(ConversionOutcome.Complete, result.Outcome);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Equal(1, page.GetProperty("patternHatchCount").GetInt32());
        var drawing = ACadSharp.IO.DwgReader.Read(output);
        Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Hatch>(), hatch => !hatch.IsSolid);
        Assert.Empty(drawing.Entities.OfType<ACadSharp.Entities.Line>());
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

        Assert.Equal(ConversionOutcome.Complete, result.Outcome);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var page = Assert.Single(json.RootElement.GetProperty("pages").EnumerateArray());
        Assert.Contains("semantic-recognition-skipped-complexity", page.GetProperty("semanticWarnings").EnumerateArray().Select(value => value.GetString()));
    }

    private static byte[] CreateMinimalPdf(string contents)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Contents 4 0 R >>",
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
