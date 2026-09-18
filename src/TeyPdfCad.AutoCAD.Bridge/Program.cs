using TeyPdfCad.AutoCAD.Bridge;

try
{
    var request = BridgeRequest.Parse(args);
    return await AutoCadExecutor.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
}
catch (Exception exception) when (exception is ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine(exception.Message);
    return BridgeProtocol.InvalidRequestExitCode;
}
