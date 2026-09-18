using System.Security.Cryptography;
using System.Text;

namespace TeyPdfCad.Web.Jobs;

public sealed record ConversionJob(
    string JobId,
    string PdfSha256,
    string PipelineVersion,
    ConversionSettings Settings)
{
    public string IdempotencyKey => BuildIdempotencyKey(PdfSha256, PipelineVersion, Settings);

    public static ConversionJob Create(
        string jobId,
        string pdfSha256,
        string pipelineVersion,
        ConversionSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job id is required.", nameof(jobId));
        if (jobId.Length > 128 || jobId.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException(
                "Job id may contain only letters, digits, '-' and '_' and must be at most 128 characters.",
                nameof(jobId));
        if (string.IsNullOrWhiteSpace(pipelineVersion))
            throw new ArgumentException("Pipeline version is required.", nameof(pipelineVersion));
        if (pipelineVersion.Trim().Length > 128)
            throw new ArgumentException("Pipeline version must be at most 128 characters.", nameof(pipelineVersion));

        var normalizedSettings = settings ?? ConversionSettings.Default;
        var settingsError = normalizedSettings.Validate();
        if (settingsError is not null)
            throw new ArgumentException(settingsError, nameof(settings));

        return new ConversionJob(
            jobId.Trim(),
            NormalizeSha256(pdfSha256),
            pipelineVersion.Trim(),
            normalizedSettings);
    }

    public static string ComputePdfSha256(Stream pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var originalPosition = pdf.CanSeek ? pdf.Position : (long?)null;
        if (pdf.CanSeek)
            pdf.Position = 0;

        using var sha256 = SHA256.Create();
        try
        {
            return Convert.ToHexString(sha256.ComputeHash(pdf)).ToLowerInvariant();
        }
        finally
        {
            if (originalPosition.HasValue)
                pdf.Position = originalPosition.Value;
        }
    }

    private static string BuildIdempotencyKey(
        string pdfSha256,
        string pipelineVersion,
        ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(pipelineVersion) || pipelineVersion.Trim().Length > 128)
            throw new ArgumentException("Pipeline version must be between 1 and 128 characters.", nameof(pipelineVersion));
        var settingsError = settings.Validate();
        if (settingsError is not null)
            throw new ArgumentException(settingsError, nameof(settings));

        var canonical = new StringBuilder();
        AppendField(canonical, "pdfSha256", NormalizeSha256(pdfSha256));
        AppendField(canonical, "pipelineVersion", pipelineVersion.Trim());

        foreach (var pair in settings.ToCanonicalMap().OrderBy(x => x.Key, StringComparer.Ordinal))
            AppendField(canonical, pair.Key, pair.Value);

        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        builder.Append(name.Length).Append(':').Append(name)
            .Append(value.Length).Append(':').Append(value);
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("PDF SHA-256 must be a 64-character hexadecimal value.", nameof(value));
        return normalized;
    }
}
