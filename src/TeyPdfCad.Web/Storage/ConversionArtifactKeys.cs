namespace TeyPdfCad.Web.Storage;

public static class ConversionArtifactKeys
{
    public static string InputPdf(string jobId) => $"jobs/{jobId}/input.pdf";

    public static string OutputDwg(string jobId) => $"jobs/{jobId}/result.dwg";
}
