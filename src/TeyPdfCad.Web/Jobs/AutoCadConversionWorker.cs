using TeyPdfCad.Web.Storage;

namespace TeyPdfCad.Web.Jobs;

public sealed class AutoCadConversionWorker : IConversionWorker
{
    private readonly IConversionArtifactStore _artifacts;
    private readonly IAutoCadHostBridge _bridge;

    public AutoCadConversionWorker(
        IConversionArtifactStore artifacts,
        IAutoCadHostBridge bridge)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        try
        {
            await using var input = await _artifacts
                .OpenReadAsync(ConversionArtifactKeys.InputPdf(job.JobId), cancellationToken)
                .ConfigureAwait(false);
            if (input is null)
                return ConversionResult.Failed(job.JobId, "input_artifact_missing", "The uploaded PDF artifact was not found.");

            await using var output = await _bridge
                .ConvertPdfAsync(input, job, cancellationToken)
                .ConfigureAwait(false);
            if (output is null)
                return ConversionResult.Failed(job.JobId, "autocad_empty_output", "The AutoCAD bridge returned no DWG stream.");

            var outputKey = ConversionArtifactKeys.OutputDwg(job.JobId);
            await _artifacts.StoreAsync(outputKey, output, cancellationToken).ConfigureAwait(false);
            return ConversionResult.Completed(job.JobId, outputKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ConversionResult.Cancelled(job.JobId);
        }
        catch (Exception exception)
        {
            return ConversionResult.Failed(job.JobId, "autocad_worker_error", exception.Message);
        }
    }
}
