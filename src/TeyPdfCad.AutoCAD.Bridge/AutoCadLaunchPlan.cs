using System.Globalization;
using System.Text;

namespace TeyPdfCad.AutoCAD.Bridge;

internal sealed record AutoCadLaunchPlan(IReadOnlyList<string> Arguments)
{
    public static AutoCadLaunchPlan Create(
        string inputDrawing,
        string script,
        string isolatedUserData,
        string isolationKey)
        => new(
        [
            "/i", inputDrawing,
            "/s", script,
            "/isolate", isolationKey, isolatedUserData,
            "/l", "ru-RU"
        ]);

    public static IReadOnlyList<string> CreateScriptLines(
        string pluginDll,
        string inputPdf,
        string outputDwg,
        double importScale)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        return
        [
            "_.FILEDIA", "0",
            "_.CMDECHO", "1",
            "_.SECURELOAD", "0",
            "_.NETLOAD", Quote(pluginDll),
            "_.-PDFIMPORT", "_F", Quote(inputPdf), "1", "0,0",
            importScale.ToString("R", CultureInfo.InvariantCulture), "0",
            "_.TEYPDFDUMPALL",
            "_.TEYPDFRECONSTRUCTALL",
            "_.SAVEAS", "2018", Quote(outputDwg),
            "_.TEYPDFBRIDGECOMPLETE",
        ];
    }

    public static void WriteScript(
        string path,
        string pluginDll,
        string inputPdf,
        string outputDwg,
        double importScale)
        => File.WriteAllLines(
            path,
            CreateScriptLines(pluginDll, inputPdf, outputDwg, importScale),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static double ParseImportScale(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return 1d;

        if (!double.TryParse(
                configured.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var scale)
            || !double.IsFinite(scale)
            || scale <= 0d)
        {
            throw new InvalidOperationException(
                "TEYPDFCAD_AUTOCAD_PDFIMPORT_SCALE must be a positive invariant number.");
        }

        return scale;
    }
}
