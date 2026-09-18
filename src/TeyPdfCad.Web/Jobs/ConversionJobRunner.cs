namespace TeyPdfCad.Web.Jobs;

public sealed class ConversionJobRunner
{
    private readonly IConversionJobQueue _queue;
    private readonly IConversionWorker _worker;
    private readonly IConversionJobRegistry _jobs;
    private readonly IConversionResultRegistry? _results;

    public ConversionJobRunner(
        IConversionJobQueue queue,
        IConversionWorker worker,
        IConversionJobRegistry jobs,
        IConversionResultRegistry? results = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _results = results;
    }

    public async Task<ConversionResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var job = await _queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _worker.ConvertAsync(job, cancellationToken).ConfigureAwait(false);
            ValidateResult(job, result);
            Complete(job, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var result = ConversionResult.Cancelled(job.JobId);
            Complete(job, result);
            return result;
        }
        catch (Exception exception)
        {
            var result = ConversionResult.Failed(job.JobId, "worker_error", exception.Message);
            Complete(job, result);
            return result;
        }
    }

    private void Complete(ConversionJob job, ConversionResult result)
    {
        PublishBeforeCompletion(result);
        if (!_queue.TryComplete(job, result.Status))
        {
            _results?.TryRemove(result.JobId, out _);
            throw new InvalidOperationException("Job lifecycle did not remain Processing.");
        }

        _jobs.MarkTerminal(job);
    }

    private void PublishBeforeCompletion(ConversionResult result)
    {
        _results?.Set(result);
    }

    private static void ValidateResult(ConversionJob job, ConversionResult? result)
    {
        if (result is null || !string.Equals(result.JobId, job.JobId, StringComparison.Ordinal))
            throw new InvalidOperationException("Worker returned a result for another job.");

        switch (result.Status)
        {
            case ConversionJobStatus.Completed:
                if (string.IsNullOrWhiteSpace(result.OutputArtifactKey))
                    throw new InvalidOperationException("Completed worker result requires an output artifact.");
                break;
            case ConversionJobStatus.Failed:
                if (string.IsNullOrWhiteSpace(result.FailureCode))
                    throw new InvalidOperationException("Failed worker result requires a failure code.");
                break;
            case ConversionJobStatus.Cancelled:
                break;
            default:
                throw new InvalidOperationException("Worker result must have a valid terminal status.");
        }
    }
}
