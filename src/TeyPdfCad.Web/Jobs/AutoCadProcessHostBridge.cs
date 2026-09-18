using System.Diagnostics;
using System.Text;

namespace TeyPdfCad.Web.Jobs;

public sealed class AutoCadProcessHostBridge : IAutoCadHostBridge
{
    private readonly AutoCadHostBridgeOptions _options;

    public AutoCadProcessHostBridge(AutoCadHostBridgeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!File.Exists(_options.ExecutablePath))
            throw new FileNotFoundException("Configured AutoCAD bridge executable was not found.", _options.ExecutablePath);
    }

    public async Task<Stream> ConvertPdfAsync(
        Stream inputPdf,
        ConversionJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPdf);
        ArgumentNullException.ThrowIfNull(job);

        var bridgeRoot = Path.Combine(Path.GetTempPath(), "TeyPdfCad", "bridge");
        var workDirectory = Path.GetFullPath(Path.Combine(
            bridgeRoot,
            $"{job.JobId}-{Guid.NewGuid():N}"));
        var rootWithSeparator = Path.GetFullPath(bridgeRoot) + Path.DirectorySeparatorChar;
        if (!workDirectory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AutoCAD bridge work directory escaped its temp root.");
        Directory.CreateDirectory(workDirectory);
        var inputPath = Path.Combine(workDirectory, "input.pdf");
        var outputPath = Path.Combine(workDirectory, "result.dwg");

        try
        {
            await using (var input = new FileStream(
                inputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await inputPdf.CopyToAsync(input, cancellationToken).ConfigureAwait(false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                WorkingDirectory = workDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("--protocol-version");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);
            startInfo.ArgumentList.Add("--job-id");
            startInfo.ArgumentList.Add(job.JobId);
            startInfo.ArgumentList.Add("--pipeline-version");
            startInfo.ArgumentList.Add(job.PipelineVersion);
            startInfo.ArgumentList.Add("--output-units");
            startInfo.ArgumentList.Add(job.Settings.OutputUnits);
            startInfo.ArgumentList.Add("--preserve-source-geometry");
            startInfo.ArgumentList.Add(job.Settings.PreserveSourceGeometry ? "true" : "false");
            startInfo.ArgumentList.Add("--recognizer-profile");
            startInfo.ArgumentList.Add(job.Settings.RecognizerProfile);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the AutoCAD bridge process.");
            var stdoutTask = ReadLimitedAsync(process.StandardOutput, cancellationToken);
            var stderrTask = ReadLimitedAsync(process.StandardError, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                await TryKillAndWaitAsync(process).ConfigureAwait(false);
                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"AutoCAD bridge exited with code {process.ExitCode}. stderr: {Trim(stderr)} stdout: {Trim(stdout)}");

            if (!await IsDwgAsync(outputPath, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("AutoCAD bridge did not produce a valid DWG header.");

            return new DeleteOnDisposeFileStream(outputPath, workDirectory);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private static string Trim(string value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();

    private static async Task<string> ReadLimitedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        const int maxCharacters = 16 * 1024;
        var buffer = new char[4096];
        var output = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length < maxCharacters)
            {
                var remaining = maxCharacters - output.Length;
                output.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return output.ToString();
    }

    private static async Task<bool> IsDwgAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[6];
        var read = await stream.ReadAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        return read == header.Length &&
            header[0] == (byte)'A' && header[1] == (byte)'C' && header[2] == (byte)'1' &&
            header[3] == (byte)'0' && header[4] >= (byte)'0' && header[4] <= (byte)'9' &&
            header[5] >= (byte)'0' && header[5] <= (byte)'9';
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task TryKillAndWaitAsync(Process process)
    {
        TryKill(process);
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    private sealed class DeleteOnDisposeFileStream : FileStream
    {
        private readonly string _workDirectory;

        public DeleteOnDisposeFileStream(string path, string workDirectory)
            : base(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
        {
            _workDirectory = workDirectory;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                TryDeleteDirectory(_workDirectory);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            TryDeleteDirectory(_workDirectory);
            GC.SuppressFinalize(this);
        }
    }
}
