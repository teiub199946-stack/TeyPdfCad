namespace TeyPdfCad.Web.Jobs;

public sealed record ConversionResult(
    string JobId,
    ConversionJobStatus Status,
    string? OutputArtifactKey,
    string? FailureCode,
    string? FailureMessage)
{
    public static ConversionResult Completed(string jobId, string outputArtifactKey)
        => new(jobId, ConversionJobStatus.Completed, outputArtifactKey, null, null);

    public static ConversionResult Failed(string jobId, string failureCode, string failureMessage)
        => new(jobId, ConversionJobStatus.Failed, null, failureCode, failureMessage);

    public static ConversionResult Cancelled(string jobId)
        => new(jobId, ConversionJobStatus.Cancelled, null, "cancelled", "The conversion was cancelled.");
}
