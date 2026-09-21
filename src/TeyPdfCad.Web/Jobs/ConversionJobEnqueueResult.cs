namespace TeyPdfCad.Web.Jobs;

public enum ConversionJobEnqueueResult
{
    Accepted,
    Duplicate,
    CapacityExceeded,
}
