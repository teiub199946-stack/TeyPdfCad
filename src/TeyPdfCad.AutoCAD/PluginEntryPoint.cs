using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(TeyPdfCad.AutoCAD.PluginEntryPoint))]
[assembly: CommandClass(typeof(TeyPdfCad.AutoCAD.ReconstructionCommands))]

namespace TeyPdfCad.AutoCAD;

public sealed class PluginEntryPoint : IExtensionApplication
{
    public void Initialize()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        document?.Editor.WriteMessage(
            "\nTeyPdfCad loaded successfully. Commands: TEYPDFPING, TEYPDFANALYZE, TEYPDFDUMP, TEYPDFRECONSTRUCT.\n");
    }

    public void Terminate()
    {
    }
}
