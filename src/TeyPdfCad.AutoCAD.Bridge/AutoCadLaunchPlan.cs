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
        string outputDwg)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        return
        [
            "_.FILEDIA", "0",
            "_.CMDECHO", "1",
            "_.SECURELOAD", "0",
            "_.NETLOAD", Quote(pluginDll),
            "_.-PDFIMPORT", "_F", Quote(inputPdf), "1", "0,0", "25.4", "0",
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
        string outputDwg)
        => File.WriteAllLines(
            path,
            CreateScriptLines(pluginDll, inputPdf, outputDwg),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
