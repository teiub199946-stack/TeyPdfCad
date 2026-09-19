using TeyPdfCad.Web.Jobs;
using Xunit;

namespace TeyPdfCad.Web.Tests;

public sealed class AutoCadRuntimeReadinessTests
{
    [Fact]
    public void Rejects_missing_configuration_after_all_other_paths_exist()
    {
        var root = Path.Combine(Path.GetTempPath(), "TeyPdfCad", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bridge = Path.Combine(root, "bridge.exe");
            var core = Path.Combine(root, "accoreconsole.exe");
            var plugin = Path.Combine(root, "TeyPdfCad.AutoCAD.dll");
            File.WriteAllBytes(bridge, [1]);
            File.WriteAllBytes(core, [1]);
            File.WriteAllBytes(plugin, [1]);

            var result = AutoCadRuntimeReadiness.Evaluate(bridge, core, plugin);

            Assert.False(result.IsReady);
            Assert.Equal("autocad_configuration_not_found", result.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Accepts_complete_runtime_file_set()
    {
        var root = Path.Combine(Path.GetTempPath(), "TeyPdfCad", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bridge = Path.Combine(root, "bridge.exe");
            var core = Path.Combine(root, "accoreconsole.exe");
            var plugin = Path.Combine(root, "TeyPdfCad.AutoCAD.dll");
            File.WriteAllBytes(bridge, [1]);
            File.WriteAllBytes(core, [1]);
            File.WriteAllBytes(plugin, [1]);
            File.WriteAllBytes(Path.Combine(root, "acad2022.cfg"), [1]);

            var result = AutoCadRuntimeReadiness.Evaluate(bridge, core, plugin);

            Assert.True(result.IsReady);
            Assert.Null(result.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Accepts_explicit_user_configuration_outside_install_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "TeyPdfCad", Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "install");
        var userConfig = Path.Combine(root, "user", "rus", "acad2022.cfg");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.GetDirectoryName(userConfig)!);
        try
        {
            var bridge = Path.Combine(install, "bridge.exe");
            var core = Path.Combine(install, "accoreconsole.exe");
            var plugin = Path.Combine(install, "TeyPdfCad.AutoCAD.dll");
            File.WriteAllBytes(bridge, [1]);
            File.WriteAllBytes(core, [1]);
            File.WriteAllBytes(plugin, [1]);
            File.WriteAllBytes(userConfig, [1]);

            var result = AutoCadRuntimeReadiness.Evaluate(bridge, core, plugin, userConfig);

            Assert.True(result.IsReady);
            Assert.Null(result.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
