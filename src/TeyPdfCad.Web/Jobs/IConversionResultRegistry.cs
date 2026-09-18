namespace TeyPdfCad.Web.Jobs;

public interface IConversionResultRegistry
{
    void Set(ConversionResult result);

    bool TryGet(string jobId, out ConversionResult? result);

    bool TryRemove(string jobId, out ConversionResult? result);

    int RemoveOlderThan(TimeSpan age);
}
