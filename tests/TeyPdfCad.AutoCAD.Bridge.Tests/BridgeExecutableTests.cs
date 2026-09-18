using System.Diagnostics;
using TeyPdfCad.AutoCAD.Bridge;
using Xunit;

namespace TeyPdfCad.AutoCAD.Bridge.Tests;

public sealed class BridgeExecutableTests
{
    [Fact]
    public async Task Invalid_protocol_exits_with_request_error_code()
    {
        using var files = ExecutableTestFiles.Create();
        var result = await RunAsync(files, protocolVersion: "99");

        Assert.Equal(BridgeProtocol.InvalidRequestExitCode, result.ExitCode);
        Assert.Contains("Unsupported bridge protocol version", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_host_environment_exits_with_dependency_error_code()
    {
        using var files = ExecutableTestFiles.Create();
        var result = await RunAsync(files, BridgeProtocol.SupportedProtocolVersion);

        Assert.Equal(BridgeProtocol.MissingHostDependencyExitCode, result.ExitCode);
        Assert.Contains("requires TEYPDFCAD_AUTOCAD_CORE_CONSOLE", result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<ExecutionResult> RunAsync(
        ExecutableTestFiles files,
        string protocolVersion)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "TeyPdfCad.AutoCAD.Bridge.exe");
        Assert.True(File.Exists(executable), $"Bridge executable was not found: {executable}");

        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment.Remove("TEYPDFCAD_AUTOCAD_CORE_CONSOLE");
        info.Environment.Remove("TEYPDFCAD_AUTOCAD_PLUGIN_DLL");
        AddPair(info, "--input", files.Input);
        AddPair(info, "--protocol-version", protocolVersion);
        AddPair(info, "--output", files.Output);
        AddPair(info, "--job-id", "bridge-contract-test");
        AddPair(info, "--pipeline-version", "v1");
        AddPair(info, "--output-units", "drawing");
        AddPair(info, "--preserve-source-geometry", "true");
        AddPair(info, "--recognizer-profile", "default");

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Failed to start the bridge contract test process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ExecutionResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static void AddPair(ProcessStartInfo info, string name, string value)
    {
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(value);
    }

    private sealed record ExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class ExecutableTestFiles : IDisposable
    {
        private ExecutableTestFiles(string directory)
        {
            Directory = directory;
            System.IO.Directory.CreateDirectory(directory);
            Input = Path.Combine(directory, "input.pdf");
            Output = Path.Combine(directory, "result.dwg");
            File.WriteAllBytes(Input, [0x25, 0x50, 0x44, 0x46]);
        }

        public string Directory { get; }
        public string Input { get; }
        public string Output { get; }

        public static ExecutableTestFiles Create()
            => new(Path.Combine(Path.GetTempPath(), "TeyPdfCad.Bridge.Tests", Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
