using System.Diagnostics;
using System.Text;
using TeyPdfCad.Core.Bridge;

namespace TeyPdfCad.AutoCAD.Bridge;

internal static class AutoCadExecutor
{
    public static async Task<int> RunAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var coreConsole = Environment.GetEnvironmentVariable("TEYPDFCAD_AUTOCAD_CORE_CONSOLE");
        var pluginDll = Environment.GetEnvironmentVariable("TEYPDFCAD_AUTOCAD_PLUGIN_DLL");
        var profile = Environment.GetEnvironmentVariable("TEYPDFCAD_AUTOCAD_PROFILE");
        if (string.IsNullOrWhiteSpace(coreConsole) || string.IsNullOrWhiteSpace(pluginDll))
        {
            Console.Error.WriteLine(
                "AutoCAD executor requires TEYPDFCAD_AUTOCAD_CORE_CONSOLE and " +
                "TEYPDFCAD_AUTOCAD_PLUGIN_DLL.");
            return BridgeProtocol.MissingHostDependencyExitCode;
        }

        coreConsole = Path.GetFullPath(coreConsole);
        pluginDll = Path.GetFullPath(pluginDll);
        if (!File.Exists(coreConsole))
        {
            Console.Error.WriteLine($"AutoCAD Core Console was not found: {coreConsole}");
            return BridgeProtocol.MissingHostDependencyExitCode;
        }
        if (!File.Exists(pluginDll))
        {
            Console.Error.WriteLine($"TeyPdfCad plugin DLL was not found: {pluginDll}");
            return BridgeProtocol.MissingHostDependencyExitCode;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad",
            "bridge",
            $"{request.JobId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var inputPdf = Path.Combine(root, "input.pdf");
        var outputDwg = Path.Combine(root, "result.dwg");
        var script = Path.Combine(root, "run.scr");
        var log = Path.Combine(root, "autocad.log");
        var status = Path.Combine(root, "bridge-status.txt");
        try
        {
            File.Copy(request.InputPath, inputPdf, overwrite: true);
            WriteScript(script, pluginDll, inputPdf, outputDwg);

            var info = new ProcessStartInfo
            {
                FileName = coreConsole,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.Environment[BridgeEnvironmentVariables.PipelineVersion] = request.PipelineVersion;
            info.Environment[BridgeEnvironmentVariables.OutputUnits] = request.OutputUnits;
            info.Environment[BridgeEnvironmentVariables.PreserveSourceGeometry] =
                request.PreserveSourceGeometry ? "true" : "false";
            info.Environment[BridgeEnvironmentVariables.RecognizerProfile] = request.RecognizerProfile;
            info.Environment[BridgeEnvironmentVariables.StatusFile] = status;
            if (HasExplicitProfile(profile))
            {
                info.ArgumentList.Add("/p");
                info.ArgumentList.Add(profile!.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(profile))
            {
                Console.Error.WriteLine(
                    $"AutoCAD profile '{profile}' is unnamed; starting Core Console with its default profile.");
            }
            info.ArgumentList.Add("/s");
            info.ArgumentList.Add(script);

            using var process = Process.Start(info);
            if (process is null)
            {
                Console.Error.WriteLine("Failed to start AutoCAD Core Console.");
                return BridgeProtocol.ProcessFailureExitCode;
            }

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            await File.WriteAllTextAsync(log, output + Environment.NewLine + errors, Encoding.UTF8, CancellationToken.None)
                .ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"AutoCAD Core Console exited with code {process.ExitCode}. Log: {log}");
                return BridgeProtocol.ProcessFailureExitCode;
            }

            if (!BridgeProtocol.TryReadSuccessStatus(status, out var statusError))
            {
                Console.Error.WriteLine($"AutoCAD reconstruction failed: {statusError}. Log: {log}");
                return BridgeProtocol.ReconstructionFailureExitCode;
            }

            if (!File.Exists(outputDwg) || !await IsDwgAsync(outputDwg, cancellationToken).ConfigureAwait(false))
            {
                Console.Error.WriteLine($"AutoCAD executor did not produce a valid DWG. Log: {log}");
                return BridgeProtocol.ReconstructionFailureExitCode;
            }

            File.Copy(outputDwg, request.OutputPath, overwrite: true);
            Console.WriteLine($"AutoCAD conversion completed. Log: {log}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"AutoCAD executor cancelled. Log: {log}");
            return BridgeProtocol.CancelledExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AutoCAD executor failed: {exception.Message}. Log: {log}");
            return BridgeProtocol.ProcessFailureExitCode;
        }
        finally
        {
            var preserveLogs = string.Equals(
                Environment.GetEnvironmentVariable("TEYPDFCAD_AUTOCAD_PRESERVE_LOGS"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (!preserveLogs)
                TryDelete(root);
        }
    }

    private static void WriteScript(string path, string pluginDll, string inputPdf, string outputDwg)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var lines = new[]
        {
            "_.FILEDIA", "0",
            "_.CMDECHO", "1",
            "_.NETLOAD", Quote(pluginDll),
            "_.-PDFIMPORT", "F", Quote(inputPdf), "1", "0,0", "1", "0",
            "_.TEYPDFDUMPALL",
            "_.TEYPDFRECONSTRUCTALL",
            "_.SAVEAS", "2018", Quote(outputDwg),
            "_.QUIT", "Y",
        };
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static async Task<bool> IsDwgAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[6];
        var read = await stream.ReadAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        return BridgeProtocol.IsDwgHeader(header[..read]);
    }

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 2_000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static bool HasExplicitProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return false;

        var normalized = profile.Trim();
        return !string.Equals(normalized, "<<Профиль без имени>>", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalized, "<<Unnamed Profile>>", StringComparison.OrdinalIgnoreCase);
    }
}
