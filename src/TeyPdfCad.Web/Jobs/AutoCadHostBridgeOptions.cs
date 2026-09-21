namespace TeyPdfCad.Web.Jobs;

public sealed record AutoCadHostBridgeOptions(
    string ExecutablePath,
    TimeSpan Timeout)
{
    public static AutoCadHostBridgeOptions FromConfiguration(
        string? executablePath,
        string? timeoutSeconds)
    {
        var path = executablePath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("TEYPDFCAD_AUTOCAD_BRIDGE_EXE is required for the AutoCAD process bridge.");

        var timeout = TimeSpan.FromMinutes(10);
        if (timeoutSeconds is not null)
        {
            if (!int.TryParse(timeoutSeconds, out var seconds) || seconds <= 0)
                throw new InvalidOperationException(
                    "TEYPDFCAD_AUTOCAD_TIMEOUT_SECONDS must be a positive integer when specified.");
            timeout = TimeSpan.FromSeconds(seconds);
        }

        return new AutoCadHostBridgeOptions(Path.GetFullPath(path), timeout);
    }
}
