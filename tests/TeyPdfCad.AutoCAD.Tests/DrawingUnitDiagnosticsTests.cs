using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class DrawingUnitDiagnosticsTests
{
    [Fact]
    public void Format_millimeters_reports_verified_mm_interpretation()
    {
        var output = InvokeFormat(UnitsValue.Millimeters);

        Assert.Contains("INSUNITS=4", output);
        Assert.Contains("Millimeters", output);
        Assert.Contains("1 drawing unit = 1 mm", output);
        Assert.DoesNotContain("unverified", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_undefined_does_not_claim_millimeters()
    {
        var output = InvokeFormat(UnitsValue.Undefined);

        Assert.Contains("INSUNITS=0", output);
        Assert.Contains("unverified", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1 drawing unit = 1 mm", output);
    }

    [Fact]
    public void Format_meters_reports_non_mm_drawing_units()
    {
        var output = InvokeFormat(UnitsValue.Meters);

        Assert.Contains("INSUNITS=6", output);
        Assert.Contains("Meters", output);
        Assert.Contains("not millimeters", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1 drawing unit = 1 mm", output);
    }

    private static string InvokeFormat(UnitsValue units)
    {
        var assembly = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly;
        var diagnosticsType = assembly.GetType("TeyPdfCad.AutoCAD.DrawingUnitDiagnostics");
        Assert.NotNull(diagnosticsType);

        var method = diagnosticsType!.GetMethod(
            "Format",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(UnitsValue) },
            modifiers: null);

        Assert.NotNull(method);
        var result = (string?)method!.Invoke(null, new object[] { units });
        Assert.NotNull(result);
        return result!;
    }
}
