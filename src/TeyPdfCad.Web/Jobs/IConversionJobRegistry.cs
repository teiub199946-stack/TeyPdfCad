namespace TeyPdfCad.Web.Jobs;

public interface IConversionJobRegistry
{
    bool TryAdd(ConversionJob job);
    bool TryGet(string jobId, out ConversionJob? job);
    bool TryRemove(string jobId, out ConversionJob? job);
    void MarkTerminal(ConversionJob job);
    int RemoveTerminalOlderThan(TimeSpan age);
}
