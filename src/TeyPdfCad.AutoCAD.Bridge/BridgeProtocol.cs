using System.Text;

namespace TeyPdfCad.AutoCAD.Bridge;

internal static class BridgeProtocol
{
    public const string SupportedProtocolVersion = "1";

    public const int InvalidRequestExitCode = 2;
    public const int MissingHostDependencyExitCode = 21;
    public const int ProcessFailureExitCode = 22;
    public const int ReconstructionFailureExitCode = 23;
    public const int CancelledExitCode = 24;

    public static bool IsDwgHeader(ReadOnlySpan<byte> header)
        => header.Length >= 6 &&
            header[0] == (byte)'A' && header[1] == (byte)'C' && header[2] == (byte)'1' &&
            header[3] == (byte)'0' && header[4] is >= (byte)'0' and <= (byte)'9' &&
            header[5] is >= (byte)'0' and <= (byte)'9';

    public static bool TryReadSuccessStatus(string path, out string error)
    {
        error = "AutoCAD bridge did not report a successful reconstruction.";
        try
        {
            var value = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(value))
                error = value.StartsWith("error|", StringComparison.OrdinalIgnoreCase)
                    ? value[6..]
                    : value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"status file could not be read: {exception.Message}";
        }

        return false;
    }
}
