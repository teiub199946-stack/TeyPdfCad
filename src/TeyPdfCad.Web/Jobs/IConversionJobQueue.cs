namespace TeyPdfCad.Web.Jobs;

public interface IConversionJobQueue
{
    ValueTask<ConversionJobEnqueueResult> EnqueueAsync(
        ConversionJob job,
        CancellationToken cancellationToken = default);

    ValueTask<ConversionJob> DequeueAsync(CancellationToken cancellationToken = default);

    bool TryComplete(ConversionJob job, ConversionJobStatus status);

    bool TryGetByIdempotencyKey(string idempotencyKey, out ConversionJob? job);

    bool TryCancel(ConversionJob job);

    ConversionJobStatus GetStatus(ConversionJob job);

    int RemoveTerminalOlderThan(TimeSpan age);
}
