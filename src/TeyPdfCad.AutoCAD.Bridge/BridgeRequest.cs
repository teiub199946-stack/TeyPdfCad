namespace TeyPdfCad.AutoCAD.Bridge;

internal sealed record BridgeRequest(
    string InputPath,
    string OutputPath,
    string JobId,
    string PipelineVersion,
    string OutputUnits,
    bool PreserveSourceGeometry,
    string RecognizerProfile)
{
    private static readonly HashSet<string> SupportedArguments = new(StringComparer.Ordinal)
    {
        "--input",
        "--output",
        "--job-id",
        "--protocol-version",
        "--pipeline-version",
        "--output-units",
        "--preserve-source-geometry",
        "--recognizer-profile",
    };

    public static BridgeRequest Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Bridge arguments must be '--name value' pairs.");
            if (!SupportedArguments.Contains(args[index]))
                throw new ArgumentException($"Unsupported bridge argument: {args[index]}.");
            if (!values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException($"Duplicate bridge argument: {args[index]}.");
        }

        var version = Required(values, "--protocol-version");
        if (!string.Equals(version, BridgeProtocol.SupportedProtocolVersion, StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported bridge protocol version '{version}'.");

        var input = Path.GetFullPath(Required(values, "--input"));
        var output = Path.GetFullPath(Required(values, "--output"));
        if (!File.Exists(input))
            throw new FileNotFoundException("Input PDF was not found.", input);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input and output paths must differ.");
        if (!string.Equals(Path.GetExtension(input), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input file must use the .pdf extension.");
        if (!string.Equals(Path.GetExtension(output), ".dwg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output file must use the .dwg extension.");

        if (!bool.TryParse(Required(values, "--preserve-source-geometry"), out var preserve))
            throw new ArgumentException("--preserve-source-geometry must be true or false.");

        var jobId = Required(values, "--job-id");
        if (jobId.Length > 128 || jobId.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException(
                "--job-id may contain only letters, digits, '-' and '_' and must be at most 128 characters.");

        return new BridgeRequest(
            input,
            output,
            jobId,
            Required(values, "--pipeline-version"),
            Required(values, "--output-units"),
            preserve,
            Required(values, "--recognizer-profile"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required bridge argument is missing: {name}.");
        return value;
    }
}
