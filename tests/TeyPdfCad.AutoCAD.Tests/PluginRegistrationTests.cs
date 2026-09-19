using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class PluginRegistrationTests
{
    [Fact]
    public void Bridge_completion_preserves_existing_failure_status()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "status.txt");
        try
        {
            File.WriteAllText(path, "error|reconstruction failed");

            BridgeRuntimeSettings.CompleteSavedOutput(path);

            Assert.Equal("error|reconstruction failed", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Bridge_completion_writes_ok_only_after_saved_output_command()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "status.txt");
        try
        {
            BridgeRuntimeSettings.CompleteSavedOutput(path);

            Assert.Equal("ok", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Assembly_declares_explicit_AutoCAD_plugin_entrypoint_and_command_class()
    {
        var assemblyPath = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var attributeTypeNames = metadata.GetAssemblyDefinition()
            .GetCustomAttributes()
            .Select(handle => metadata.GetCustomAttribute(handle))
            .Select(attribute => ResolveAttributeTypeName(metadata, attribute.Constructor))
            .Where(name => name is not null)
            .ToArray();

        Assert.Contains("Autodesk.AutoCAD.Runtime.ExtensionApplicationAttribute", attributeTypeNames);
        Assert.Contains("Autodesk.AutoCAD.Runtime.CommandClassAttribute", attributeTypeNames);
    }

    private static string? ResolveAttributeTypeName(MetadataReader metadata, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
            return null;

        var memberReference = metadata.GetMemberReference((MemberReferenceHandle)constructor);
        if (memberReference.Parent.Kind != HandleKind.TypeReference)
            return null;

        var typeReference = metadata.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
        var ns = metadata.GetString(typeReference.Namespace);
        var name = metadata.GetString(typeReference.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
}
