using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class PersistentCandidateIdSpikeTests
{
    private const string AppName = "TEYCONVERT_CANDIDATE_V1";

    [Fact]
    public void Xdata_round_trip_preserves_candidate_id_and_role_for_supported_entity_shapes()
    {
        var drawing = new CadDocument();
        var expected = new List<(Entity Entity, string CandidateId, string Role)>();

        Add(expected, new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), "v1:1:LINE:001", "primary");

        var dimension = new DimensionAligned(new XYZ(20, 0, 0), new XYZ(40, 0, 0))
        {
            DefinitionPoint = new XYZ(20, 10, 0)
        };
        Add(expected, dimension, "v1:1:DIMENSION:002", "primary");

        var leaderText = new TextEntity
        {
            Value = "Leader note",
            InsertPoint = new XYZ(60, 10, 0),
            Height = 2.5d
        };
        var leader = new Leader
        {
            ArrowHeadEnabled = true,
            CreationType = LeaderCreationType.CreatedWithTextAnnotation,
            PathType = LeaderPathType.StraightLineSegments,
            TextHeight = leaderText.Height
        };
        leader.Vertices.Add(new XYZ(50, 0, 0));
        leader.Vertices.Add(new XYZ(60, 10, 0));
        Add(expected, leader, "v1:1:LEADER:003", "primary");
        Add(expected, leaderText, "v1:1:LEADER:003", "annotation");

        var boundary = new LwPolyline([new XY(80, 0), new XY(100, 0), new XY(100, 20), new XY(80, 20)])
        {
            IsClosed = true,
            IsInvisible = true
        };
        drawing.Entities.Add(boundary);
        var hatch = new Hatch
        {
            IsSolid = true,
            Pattern = HatchPattern.Solid,
            SeedPoints = [new XY(90, 10)]
        };
        hatch.Paths.Add(new Hatch.BoundaryPath([boundary]));
        Add(expected, hatch, "v1:1:HATCH:004", "primary");

        var block = new BlockRecord("SPIKE_LEVEL");
        block.Entities.Add(new AttributeDefinition
        {
            Tag = "LEVEL",
            Value = string.Empty,
            InsertPoint = new XYZ(0, 0, 0),
            Height = 2.5d
        });
        drawing.BlockRecords.Add(block);

        var firstInsert = new Insert(block) { InsertPoint = new XYZ(120, 0, 0) };
        firstInsert.Attributes.Single().Value = "+0.000";
        Add(expected, firstInsert, "v1:1:LEVEL:005", "primary");
        Add(expected, firstInsert.Attributes.Single(), "v1:1:LEVEL:005", "attribute");

        var secondInsert = new Insert(block) { InsertPoint = new XYZ(140, 0, 0) };
        secondInsert.Attributes.Single().Value = "+3.600";
        Add(expected, secondInsert, "v1:1:LEVEL:006", "primary");
        Add(expected, secondInsert.Attributes.Single(), "v1:1:LEVEL:006", "attribute");

        // Attributes already belong to their Insert. Adding them to ModelSpace again is
        // invalid and would hide the persistence result behind an ownership error.
        drawing.Entities.Add(expected[0].Entity); // Line
        drawing.Entities.Add(expected[1].Entity); // Dimension
        drawing.Entities.Add(expected[2].Entity); // Leader
        drawing.Entities.Add(expected[3].Entity); // Leader annotation text
        drawing.Entities.Add(expected[4].Entity); // Hatch
        drawing.Entities.Add(expected[5].Entity); // First insert (owns its attribute)
        drawing.Entities.Add(expected[7].Entity); // Second insert (owns its attribute)

        var reopened = DwgReader.Read(new MemoryStream(Write(drawing)));

        // ModelSpace contains seven metadata-bearing entities plus the visible
        // HATCH boundary. Attributes must remain children of their Insert.
        Assert.Equal(8, reopened.Entities.Count);
        Assert.DoesNotContain(reopened.Entities, entity => entity is AttributeEntity);

        AssertMetadata(reopened.Entities.OfType<Line>().Single(), "v1:1:LINE:001", "primary");
        AssertMetadata(reopened.Entities.OfType<DimensionAligned>().Single(), "v1:1:DIMENSION:002", "primary");
        AssertMetadata(reopened.Entities.OfType<Leader>().Single(), "v1:1:LEADER:003", "primary");
        AssertMetadata(reopened.Entities.OfType<TextEntity>().Single(), "v1:1:LEADER:003", "annotation");
        AssertMetadata(reopened.Entities.OfType<Hatch>().Single(), "v1:1:HATCH:004", "primary");

        var inserts = reopened.Entities.OfType<Insert>()
            .OrderBy(insert => insert.InsertPoint.X)
            .ToArray();
        Assert.Equal(2, inserts.Length);

        AssertMetadata(inserts[0], "v1:1:LEVEL:005", "primary");
        Assert.Single(inserts[0].Attributes);
        AssertMetadata(inserts[0].Attributes.Single(), "v1:1:LEVEL:005", "attribute");

        AssertMetadata(inserts[1], "v1:1:LEVEL:006", "primary");
        Assert.Single(inserts[1].Attributes);
        AssertMetadata(inserts[1].Attributes.Single(), "v1:1:LEVEL:006", "attribute");

        Assert.True(reopened.AppIds.TryGetValue(AppName, out _));
    }

    private static void Add(
        ICollection<(Entity Entity, string CandidateId, string Role)> expected,
        Entity entity,
        string candidateId,
        string role)
    {
        entity.ExtendedData.Add(AppName, new ExtendedData(
        [
            new ExtendedDataString(candidateId),
            new ExtendedDataString(role)
        ]));
        expected.Add((entity, candidateId, role));
    }

    private static void AssertMetadata(Entity entity, string expectedCandidateId, string expectedRole)
    {
        Assert.True(entity.ExtendedData.TryGet(AppName, out var data));

        var records = data.Records.ToArray();
        Assert.Equal(2, records.Length);
        Assert.All(records, record => Assert.IsType<ExtendedDataString>(record));

        var values = records.Cast<ExtendedDataString>()
            .Select(record => record.Value)
            .ToArray();

        Assert.All(values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal(expectedCandidateId, values[0]);
        Assert.Equal(expectedRole, values[1]);
    }

    private static (string CandidateId, string Role)? ReadMetadata(Entity entity)
    {
        if (!entity.ExtendedData.TryGet(AppName, out var data))
        {
            return null;
        }

        var values = data.Records.OfType<ExtendedDataString>()
            .Select(record => record.Value)
            .ToArray();
        return values.Length == 2 ? (values[0], values[1]) : null;
    }

    private static byte[] Write(CadDocument drawing)
    {
        using var output = new MemoryStream();
        using (var writer = new DwgWriter(output, drawing))
        {
            writer.Write();
        }

        return output.ToArray();
    }
}
