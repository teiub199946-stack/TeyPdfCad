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
        var baseDrawing = Environment.GetEnvironmentVariable("TEYPDFCAD_AUTOCAD_BASE_DWG");
        if (string.IsNullOrWhiteSpace(coreConsole)
            || string.IsNullOrWhiteSpace(pluginDll)
            || string.IsNullOrWhiteSpace(baseDrawing))
        {
            Console.Error.WriteLine(
                "AutoCAD executor requires TEYPDFCAD_AUTOCAD_CORE_CONSOLE and " +
                "TEYPDFCAD_AUTOCAD_PLUGIN_DLL and TEYPDFCAD_AUTOCAD_BASE_DWG.");
            return BridgeProtocol.MissingHostDependencyExitCode;
        }

        coreConsole = Path.GetFullPath(coreConsole);
        pluginDll = Path.GetFullPath(pluginDll);
        baseDrawing = Path.GetFullPath(baseDrawing);
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
        if (!File.Exists(baseDrawing))
        {
            Console.Error.WriteLine($"AutoCAD base drawing was not found: {baseDrawing}");
            return BridgeProtocol.MissingHostDependencyExitCode;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad",
            "bridge",
            $"{request.JobId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var inputPdf = Path.Combine(root, "input.pdf");
        var inputDrawing = Path.Combine(root, "base.dwg");
        var outputDwg = Path.Combine(root, "result.dwg");
        var script = Path.Combine(root, "run.scr");
        var log = Path.Combine(root, "autocad.log");
        var status = Path.Combine(root, "bridge-status.txt");
        var isolatedUserData = Path.Combine(root, "userdata");
        try
        {
            File.Copy(request.InputPath, inputPdf, overwrite: true);
            File.Copy(baseDrawing, inputDrawing, overwrite: true);
            Directory.CreateDirectory(isolatedUserData);
            AutoCadLaunchPlan.WriteScript(script, pluginDll, inputPdf, outputDwg);

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
            var launchPlan = AutoCadLaunchPlan.Create(
                inputDrawing,
                script,
                isolatedUserData,
                request.JobId);
            foreach (var argument in launchPlan.Arguments)
                info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null)
            {
                Console.Error.WriteLine("Failed to start AutoCAD Core Console.");
                return BridgeProtocol.ProcessFailureExitCode;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var completedOutput = false;
            try
            {
                while (!process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (BridgeProtocol.TryReadSuccessStatus(status, out _)
                        && File.Exists(outputDwg)
                        && await IsDwgAsync(outputDwg, cancellationToken).ConfigureAwait(false))
                    {
                        completedOutput = true;
                        TryKill(process);
                        break;
                    }

                    if (File.Exists(status)
                        && !BridgeProtocol.TryReadSuccessStatus(status, out _))
                    {
                        TryKill(process);
                        break;
                    }

                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    process.Refresh();
                }
                if (!process.HasExited)
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

            if (!BridgeProtocol.TryReadSuccessStatus(status, out var statusError))
            {
                Console.Error.WriteLine($"AutoCAD reconstruction failed: {statusError}. Log: {log}");
                return BridgeProtocol.ReconstructionFailureExitCode;
            }

            if (!completedOutput && process.ExitCode != 0)
            {
                Console.Error.WriteLine($"AutoCAD Core Console exited with code {process.ExitCode}. Log: {log}");
                return BridgeProtocol.ProcessFailureExitCode;
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

    private static async Task<bool> IsDwgAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var header = new byte[6];
            var read = await stream.ReadAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
            return BridgeProtocol.IsDwgHeader(header[..read]);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
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

}
