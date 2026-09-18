namespace TeyPdfCad.Web.Jobs;

public interface IConversionWorker
{
    Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        CancellationToken cancellationToken = default);
}
