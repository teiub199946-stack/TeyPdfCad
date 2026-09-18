namespace TeyPdfCad.Web.Storage;

public interface IConversionArtifactRetention
{
    ValueTask<int> DeleteOlderThanAsync(
        TimeSpan age,
        CancellationToken cancellationToken = default);
}
