namespace TeyPdfCad.Web.Jobs;

public sealed record AutoCadRuntimeReadiness(bool IsReady, string? Error)
{
    public static AutoCadRuntimeReadiness Evaluate(
        string? bridgeExecutable,
        string? coreConsole,
        string? pluginDll,
        string? configurationFile = null)
    {
        if (string.IsNullOrWhiteSpace(bridgeExecutable))
            return NotReady("autocad_bridge_not_configured");
        if (!File.Exists(bridgeExecutable))
            return NotReady("autocad_bridge_not_found");
        if (string.IsNullOrWhiteSpace(coreConsole))
            return NotReady("autocad_core_console_not_configured");
        if (!File.Exists(coreConsole))
            return NotReady("autocad_core_console_not_found");
        if (string.IsNullOrWhiteSpace(pluginDll))
            return NotReady("autocad_plugin_not_configured");
        if (!File.Exists(pluginDll))
            return NotReady("autocad_plugin_not_found");

        var installDirectory = Path.GetDirectoryName(Path.GetFullPath(coreConsole));
        var resolvedConfiguration = string.IsNullOrWhiteSpace(configurationFile)
            ? string.IsNullOrWhiteSpace(installDirectory)
                ? null
                : Path.Combine(installDirectory, "acad2022.cfg")
            : Path.GetFullPath(configurationFile);
        if (string.IsNullOrWhiteSpace(resolvedConfiguration) || !File.Exists(resolvedConfiguration))
            return NotReady("autocad_configuration_not_found");

        return new AutoCadRuntimeReadiness(true, null);
    }

    private static AutoCadRuntimeReadiness NotReady(string error)
        => new(false, error);
}
