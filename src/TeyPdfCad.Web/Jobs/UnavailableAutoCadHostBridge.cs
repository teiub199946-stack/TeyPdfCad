namespace TeyPdfCad.Web.Jobs;

public sealed class UnavailableAutoCadHostBridge : IAutoCadHostBridge
{
    public Task<Stream> ConvertPdfAsync(
        Stream inputPdf,
        ConversionJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPdf);
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "AutoCAD host bridge is not configured. Complete the AutoCAD 2022 runtime gate before enabling autocad mode.");
    }
}
