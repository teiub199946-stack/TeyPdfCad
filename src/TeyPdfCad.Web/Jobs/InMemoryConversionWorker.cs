using TeyPdfCad.Web.Storage;

namespace TeyPdfCad.Web.Jobs;

public sealed class InMemoryConversionWorker : IConversionWorker
{
    private readonly Func<ConversionJob, CancellationToken, Task<ConversionResult>> _handler;
    private readonly IConversionArtifactStore? _artifacts;

    public InMemoryConversionWorker(
        Func<ConversionJob, CancellationToken, Task<ConversionResult>>? handler = null,
        IConversionArtifactStore? artifacts = null)
    {
        _artifacts = artifacts;
        _handler = handler ?? ((job, _) => Task.FromResult(
            ConversionResult.Completed(job.JobId, $"jobs/{job.JobId}/result.dwg")));
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _handler(job, cancellationToken).ConfigureAwait(false);
        if (_artifacts is not null && result.Status == ConversionJobStatus.Completed &&
            !string.IsNullOrWhiteSpace(result.OutputArtifactKey))
        {
            await using var content = new MemoryStream("AC1032\0TeyPdfCad in-memory smoke artifact"u8.ToArray());
            await _artifacts.StoreAsync(result.OutputArtifactKey, content, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
