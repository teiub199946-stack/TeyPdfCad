namespace TeyPdfCad.Web.Storage;

public sealed class FileSystemArtifactStore : IConversionArtifactStore, IConversionArtifactRetention
{
    private readonly string _rootDirectory;
    private readonly long _maxBytes;

    public FileSystemArtifactStore(string rootDirectory, long maxBytes = 512L * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Artifact root directory is required.", nameof(rootDirectory));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        _rootDirectory = Path.GetFullPath(rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_rootDirectory);
    }

    public async ValueTask StoreAsync(
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var targetPath = ResolvePath(key);
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithLimitAsync(content, output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public ValueTask<Stream?> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(key);
        if (!File.Exists(path))
            return ValueTask.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult<Stream?>(stream);
    }

    public ValueTask<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(key);
        if (!File.Exists(path))
            return ValueTask.FromResult(false);

        File.Delete(path);
        return ValueTask.FromResult(true);
    }

    public ValueTask<int> DeleteOlderThanAsync(
        TimeSpan age,
        CancellationToken cancellationToken = default)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age));

        var cutoff = DateTime.UtcNow - age;
        var deleted = 0;
        var jobsDirectory = Path.Combine(_rootDirectory, "jobs");
        if (!Directory.Exists(jobsDirectory))
            return ValueTask.FromResult(0);

        foreach (var file in Directory.EnumerateFiles(jobsDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) >= cutoff)
                continue;

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (FileNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return ValueTask.FromResult(deleted);
    }

    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Artifact key is required.", nameof(key));

        var normalizedKey = key.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedKey))
            throw new ArgumentException("Artifact key must be relative.", nameof(key));

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, normalizedKey));
        var rootWithSeparator = _rootDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Artifact key escapes the artifact root.", nameof(key));
        return fullPath;
    }

    private async Task CopyWithLimitAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > _maxBytes)
                throw new InvalidDataException($"Artifact exceeds the {_maxBytes} byte limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
