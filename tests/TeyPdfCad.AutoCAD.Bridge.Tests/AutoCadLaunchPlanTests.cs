using TeyPdfCad.AutoCAD.Bridge;
using Xunit;

namespace TeyPdfCad.AutoCAD.Bridge.Tests;

public sealed class AutoCadLaunchPlanTests
{
    [Fact]
    public void Uses_input_drawing_and_fresh_isolated_user_data()
    {
        var plan = AutoCadLaunchPlan.Create(
            @"C:\work\base.dwg",
            @"C:\work\run.scr",
            @"C:\work\userdata",
            "job-42");

        Assert.Equal(
        [
            "/i", @"C:\work\base.dwg",
            "/s", @"C:\work\run.scr",
            "/isolate", "job-42", @"C:\work\userdata",
            "/l", "ru-RU"
        ],
            plan.Arguments);
    }

    [Fact]
    public void Script_uses_global_pdf_file_keyword_and_does_not_wait_for_quit_prompt()
    {
        var lines = AutoCadLaunchPlan.CreateScriptLines(
            @"C:\plugin\TeyPdfCad.AutoCAD.dll",
            @"C:\work\input.pdf",
            @"C:\work\result.dwg");

        Assert.Contains("_F", lines);
        Assert.DoesNotContain("F", lines);
        var pdfImport = Array.IndexOf(lines.ToArray(), "_.-PDFIMPORT");
        Assert.Equal("25.4", lines[pdfImport + 5]);
        Assert.Contains("_.SECURELOAD", lines);
        var save = Array.IndexOf(lines.ToArray(), "_.SAVEAS");
        var complete = Array.IndexOf(lines.ToArray(), "_.TEYPDFBRIDGECOMPLETE");
        Assert.True(complete > save);
        Assert.DoesNotContain("_.QUIT", lines);
        Assert.DoesNotContain("Y", lines);
    }
}
