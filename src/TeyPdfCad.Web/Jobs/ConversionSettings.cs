namespace TeyPdfCad.Web.Jobs;

public sealed record ConversionSettings
{
    public static ConversionSettings Default { get; } = new();

    public string OutputUnits { get; init; } = "drawing";
    public bool PreserveSourceGeometry { get; init; } = true;
    public string RecognizerProfile { get; init; } = "default";

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(OutputUnits) || OutputUnits.Length > 32)
            return "outputUnits must be between 1 and 32 characters";
        if (string.IsNullOrWhiteSpace(RecognizerProfile) || RecognizerProfile.Length > 64)
            return "recognizerProfile must be between 1 and 64 characters";
        if (ContainsControlCharacters(OutputUnits) || ContainsControlCharacters(RecognizerProfile))
            return "conversion settings may not contain control characters";
        return null;
    }

    public IReadOnlyDictionary<string, string> ToCanonicalMap()
    {
        var error = Validate();
        if (error is not null)
            throw new ArgumentException(error, nameof(ConversionSettings));

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["outputUnits"] = OutputUnits,
            ["preserveSourceGeometry"] = PreserveSourceGeometry ? "true" : "false",
            ["recognizerProfile"] = RecognizerProfile,
        };
    }

    private static bool ContainsControlCharacters(string value)
        => value.Any(char.IsControl);
}
