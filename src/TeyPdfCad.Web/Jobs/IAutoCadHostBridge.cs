namespace TeyPdfCad.Web.Jobs;

public interface IAutoCadHostBridge
{
    Task<Stream> ConvertPdfAsync(
        Stream inputPdf,
        ConversionJob job,
        CancellationToken cancellationToken = default);
}
