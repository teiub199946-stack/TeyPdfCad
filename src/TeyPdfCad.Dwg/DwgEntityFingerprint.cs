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
                Number(dimension.Rotation)),
            DimensionAligned dimension => Join(
                "DimensionAligned",
                Point(dimension.FirstPoint),
                Point(dimension.SecondPoint),
                Point(dimension.DefinitionPoint)),
            DimensionArc dimension => Join(
                "DimensionArc",
                Point(dimension.Center),
                Point(dimension.FirstPoint),
                Point(dimension.SecondPoint),
                Point(dimension.DefinitionPoint),
                Number(dimension.StartAngle),
                Number(dimension.EndAngle)),
            Dimension dimension => Join(
                "Dimension",
                Point(dimension.DefinitionPoint),
                Number(dimension.Measurement)),
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
