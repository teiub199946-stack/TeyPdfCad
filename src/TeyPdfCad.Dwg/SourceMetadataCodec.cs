using System.Globalization;
using ACadSharp.Entities;
using ACadSharp.XData;

namespace TeyPdfCad.Dwg;

public static class SourceMetadataCodec
{
    public const string AppId = "TEYCONVERT_SOURCE_V1";

    public static void Write(
        Entity entity,
        PageSourceRef source)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Validate(source);

        if (entity.ExtendedData.TryGet(AppId, out _))
        {
            throw new InvalidOperationException(
                $"Entity already carries {AppId} source metadata.");
        }

        entity.ExtendedData.Add(
            AppId,
            new ExtendedData(
            [
                new ExtendedDataString(
                    source.PageNumber.ToString(CultureInfo.InvariantCulture)),
                new ExtendedDataString(source.SourceId)
            ]));
    }

    public static bool TryRead(
        Entity entity,
        out PageSourceRef source)
    {
        ArgumentNullException.ThrowIfNull(entity);
        source = default;

        if (!entity.ExtendedData.TryGet(AppId, out var data))
            return false;

        var records = data.Records.ToArray();
        if (records.Length != 2
            || records[0] is not ExtendedDataString page
            || records[1] is not ExtendedDataString sourceId
            || !int.TryParse(
                page.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pageNumber)
            || pageNumber <= 0
            || !IsValidSourceId(sourceId.Value))
        {
            return false;
        }

        source = new PageSourceRef(pageNumber, sourceId.Value);
        return true;
    }

    internal static bool HasSourceApp(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.ExtendedData.TryGet(AppId, out _);
    }

    internal static bool CanEncode(PageSourceRef source)
        => source.PageNumber > 0 && IsValidSourceId(source.SourceId);

    private static void Validate(PageSourceRef source)
    {
        if (source.PageNumber <= 0)
        {
            throw new ArgumentException(
                "Source metadata requires a positive page number.",
                nameof(source));
        }

        if (!IsValidSourceId(source.SourceId))
        {
            throw new ArgumentException(
                "Source metadata requires a non-empty SourceId without control characters.",
                nameof(source));
        }
    }

    private static bool IsValidSourceId(string? sourceId)
        => !string.IsNullOrWhiteSpace(sourceId)
            && sourceId.All(character => !char.IsControl(character));
}
