using System.Reflection;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class AnalysisReportFormatterTests
{
    [Fact]
    public void FormatSummary_reports_selected_count_and_warns_when_pdf_text_is_vector_geometry()
    {
        var assembly = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly;
        var formatterType = assembly.GetType("TeyPdfCad.AutoCAD.AnalysisReportFormatter");
        Assert.NotNull(formatterType);

        var method = formatterType!.GetMethod(
            "FormatSummary",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var output = (string?)method!.Invoke(
            null,
            new object[] { 35, 39, 0, 0, 0, "none", 0d });

        Assert.NotNull(output);
        Assert.Contains("selected=35", output!);
        Assert.Contains("lines=39", output);
        Assert.Contains("texts=0", output);
        Assert.Contains("dimensions=0", output);
        Assert.Contains("vector glyph geometry", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatSummary_does_not_emit_vector_text_warning_when_true_type_text_was_read()
    {
        var assembly = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly;
        var formatterType = assembly.GetType("TeyPdfCad.AutoCAD.AnalysisReportFormatter");
        Assert.NotNull(formatterType);

        var method = formatterType!.GetMethod(
            "FormatSummary",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var output = (string?)method!.Invoke(
            null,
            new object[] { 18, 24, 3, 3, 0, "3", 0.95d });

        Assert.NotNull(output);
        Assert.Contains("selected=18", output!);
        Assert.Contains("texts=3", output);
        Assert.Contains("dimensions=3", output);
        Assert.DoesNotContain("vector glyph geometry", output, StringComparison.OrdinalIgnoreCase);
    }
}
