using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class PluginRegistrationTests
{
    [Fact]
    public void Assembly_declares_explicit_AutoCAD_plugin_entrypoint_and_command_class()
    {
        var assembly = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly;
        var attributeTypeNames = assembly.GetCustomAttributesData()
            .Select(x => x.AttributeType.FullName)
            .ToArray();

        Assert.Contains("Autodesk.AutoCAD.Runtime.ExtensionApplicationAttribute", attributeTypeNames);
        Assert.Contains("Autodesk.AutoCAD.Runtime.CommandClassAttribute", attributeTypeNames);
    }
}
