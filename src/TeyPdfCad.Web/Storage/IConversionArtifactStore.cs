namespace TeyPdfCad.Web.Storage;

public interface IConversionArtifactStore
{
    ValueTask StoreAsync(
        string key,
        Stream content,
        CancellationToken cancellationToken = default);

    ValueTask<Stream?> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}
