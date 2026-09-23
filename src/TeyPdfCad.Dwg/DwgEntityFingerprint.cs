using System.Globalization;
using System.Text;
using ACadSharp.Entities;
using CSMath;

namespace TeyPdfCad.Dwg;

public static class DwgEntityFingerprint
{
    public static string ComputeGeometry(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return entity switch
        {
            Line line => Join("Line", Point(line.StartPoint), Point(line.EndPoint)),
            DimensionLinear dimension => Join(
                "DimensionLinear",
                Point(dimension.FirstPoint),
                Point(dimension.SecondPoint),
                Point(dimension.DefinitionPoint),
                Number(dimension.Rotation),
                DimensionTextGeometry(dimension)),
            DimensionAligned dimension => Join(
                "DimensionAligned",
                Point(dimension.FirstPoint),
                Point(dimension.SecondPoint),
                Point(dimension.DefinitionPoint),
                DimensionTextGeometry(dimension)),
            DimensionArc dimension => Join(
                "DimensionArc",
                Point(dimension.Center),
                Point(dimension.FirstPoint),
                Point(dimension.SecondPoint),
                Point(dimension.DefinitionPoint),
                Number(dimension.StartAngle),
                Number(dimension.EndAngle),
                DimensionTextGeometry(dimension)),
            Dimension dimension => Join(
                "Dimension",
                Point(dimension.DefinitionPoint),
                Number(dimension.Measurement),
                DimensionTextGeometry(dimension)),
            Leader leader => Join(
                "Leader",
                string.Join(";", leader.Vertices.Select(Point)),
                Number(leader.TextHeight),
                leader.ArrowHeadEnabled ? "1" : "0"),
            AttributeEntity attribute => Join(
                "Attribute",
                Point(attribute.InsertPoint),
                Number(attribute.Height),
                Number(attribute.Rotation),
                attribute.Tag ?? string.Empty,
                attribute.Value ?? string.Empty),
            TextEntity text => Join(
                "Text",
                Point(text.InsertPoint),
                Number(text.Height),
                Number(text.Rotation),
                text.Value ?? string.Empty),
            Insert insert => Join(
                "Insert",
                insert.Block?.Name ?? string.Empty,
                Point(insert.InsertPoint),
                Number(insert.XScale),
                Number(insert.YScale),
                Number(insert.ZScale),
                Number(insert.Rotation)),
            Hatch hatch => Join(
                "Hatch",
                hatch.IsSolid ? "1" : "0",
                Number(hatch.PatternAngle),
                Number(hatch.PatternScale),
                ComputeHatchBoundary(hatch)),
            LwPolyline polyline => Join(
                "LwPolyline",
                polyline.IsClosed ? "1" : "0",
                string.Join(";", polyline.Vertices.Select(vertex =>
                    Number(vertex.Location.X) + "," + Number(vertex.Location.Y)))),
            _ => Join(entity.GetType().Name, entity.ObjectName ?? string.Empty)
        };
    }

    public static string ComputeDimensionStyle(Dimension dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);

        var style = dimension.Style;
        if (style is null)
            return Join("DimensionStyle", "<null>");

        return Join(
            "DimensionStyle",
            style.Name ?? string.Empty,
            Number(style.LinearScaleFactor),
            Number(style.TextHeight),
            Number(style.ArrowSize),
            Number(style.TickSize),
            Number(style.DimensionLineExtension),
            Number(style.ExtensionLineOffset),
            Number(style.ExtensionLineExtension),
            Number(style.ScaleFactor),
            style.SuppressFirstDimensionLine ? "1" : "0",
            style.SuppressSecondDimensionLine ? "1" : "0",
            style.SuppressFirstExtensionLine ? "1" : "0",
            style.SuppressSecondExtensionLine ? "1" : "0",
            style.SuppressOutsideExtensions ? "1" : "0",
            style.PostFix ?? string.Empty,
            style.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
            style.DimensionLineColor.ToString(),
            style.ExtensionLineColor.ToString(),
            style.TextColor.ToString(),
            style.DimensionLineWeight.ToString(),
            style.ExtensionLineWeight.ToString(),
            style.SeparateArrowBlocks ? "1" : "0",
            style.ArrowBlock?.Name ?? string.Empty,
            style.DimArrow1?.Name ?? string.Empty,
            style.DimArrow2?.Name ?? string.Empty,
            style.Style?.Name ?? string.Empty,
            style.Style?.Filename ?? string.Empty,
            Number(style.Style?.Height ?? 0d),
            Number(style.Style?.Width ?? 0d),
            style.LineType?.Name ?? string.Empty,
            style.LineTypeExt1?.Name ?? string.Empty,
            style.LineTypeExt2?.Name ?? string.Empty);
    }

    public static string ComputeBlockDefinition(Insert insert)
    {
        ArgumentNullException.ThrowIfNull(insert);

        if (insert.Block is null)
            return Join("BlockDefinition", "<null>");

        var entityFingerprints = insert.Block.Entities
            .Select(ComputeOutput)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return Join(
            "BlockDefinition",
            insert.Block.Name ?? string.Empty,
            entityFingerprints.Length.ToString(CultureInfo.InvariantCulture),
            string.Join(";", entityFingerprints));
    }

    public static string ComputeHatchBoundary(Hatch hatch)
    {
        ArgumentNullException.ThrowIfNull(hatch);

        var exploded = hatch.Explode()
            .Select(ComputeGeometry)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return Join(
            "HatchBoundary",
            hatch.Paths.Count.ToString(CultureInfo.InvariantCulture),
            string.Join(";", exploded));
    }

    public static string ComputeOutput(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var candidateMetadata = CandidateMetadataCodec.TryRead(entity, out var candidate)
            ? Join(candidate.CandidateId, candidate.Role)
            : CandidateMetadataCodec.HasCandidateApp(entity)
                ? "<malformed-candidate-metadata>"
                : string.Empty;
        var sourceMetadata = SourceMetadataCodec.TryRead(entity, out var source)
            ? Join(
                source.PageNumber.ToString(CultureInfo.InvariantCulture),
                source.SourceId)
            : SourceMetadataCodec.HasSourceApp(entity)
                ? "<malformed-source-metadata>"
                : string.Empty;
        return Join(
            entity.GetType().Name,
            ComputeGeometry(entity),
            entity.Layer?.Name ?? string.Empty,
            entity.LineType?.Name ?? string.Empty,
            entity.LineWeight.ToString(),
            Number(entity.LineTypeScale),
            entity.Color.ToString(),
            entity.IsInvisible ? "1" : "0",
            candidateMetadata,
            sourceMetadata);
    }

    private static string DimensionTextGeometry(Dimension dimension)
        => Join(
            dimension.IsTextUserDefinedLocation ? "1" : "0",
            dimension.IsTextUserDefinedLocation
                ? Point(dimension.TextMiddlePoint)
                : "<auto>",
            Number(dimension.TextRotation),
            dimension.FlipArrow1 ? "1" : "0",
            dimension.FlipArrow2 ? "1" : "0",
            Point(dimension.Normal));

    private static string Point(XYZ point)
        => Number(point.X) + "," + Number(point.Y) + "," + Number(point.Z);

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Join(params string[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder
                .Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value)
                .Append('|');
        }

        return builder.ToString();
    }
}
