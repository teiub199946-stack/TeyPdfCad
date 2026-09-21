using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using System.Text;

[assembly: ExtensionApplication(typeof(TeyPdfCad.AutoCAD.PluginEntryPoint))]
[assembly: CommandClass(typeof(TeyPdfCad.AutoCAD.ReconstructionCommands))]
[assembly: CommandClass(typeof(TeyPdfCad.AutoCAD.PluginEntryPoint))]

namespace TeyPdfCad.AutoCAD;

public sealed class PluginEntryPoint : IExtensionApplication
{
    public void Initialize()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        document?.Editor.WriteMessage(
            "\nTeyPdfCad loaded successfully. Commands: TEYPDFPING, TEYPDFANALYZE, TEYPDFDUMP, " +
            "TEYPDFDUMPALL, TEYPDFRECONSTRUCT, TEYPDFRECONSTRUCTALL, TEYPDFSHEETCONFIG, " +
            "TEYPDFSHEETAUDIT.\n");
    }

    public void Terminate()
    {
    }

    [CommandMethod("TEYPDFHEALTH")]
    public void Health()
    {
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nTeyPdfCad health: ok.\n");

        var sentinelPath = Environment.GetEnvironmentVariable("TEYPDFCAD_HEALTH_FILE");
        if (string.IsNullOrWhiteSpace(sentinelPath))
            return;

        File.WriteAllText(
            sentinelPath,
            $"ok{Environment.NewLine}{DateTime.UtcNow:O}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
