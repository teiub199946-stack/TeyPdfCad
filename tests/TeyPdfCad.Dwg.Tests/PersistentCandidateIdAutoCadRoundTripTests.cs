using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.XData;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

/// <summary>
/// Regression evidence from the user's manual AutoCAD 2022 save/reopen of the
/// Spike D drawing. This covers the external application path, not just
/// ACadSharp's own writer/reader path.
/// </summary>
public sealed class PersistentCandidateIdAutoCadRoundTripTests
{
    private const string AppName = "TEYCONVERT_CANDIDATE_V1";

    [Fact]
    public void User_saved_AutoCAD_2022_fixture_preserves_candidate_metadata()
    {
        var fixture = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "spike-d-autocad2022-roundtrip.dwg.b64");

        var bytes = Convert.FromBase64String(File.ReadAllText(fixture));
        var reopened = DwgReader.Read(new MemoryStream(bytes));

        Assert.True(reopened.AppIds.TryGetValue(AppName, out _));

        AssertMetadata(
            reopened.Entities.OfType<Line>().Single(entity => HasMetadata(entity, "v1:1:LINE:001", "primary")),
            "v1:1:LINE:001",
            "primary");
        AssertMetadata(
            reopened.Entities.OfType<DimensionAligned>().Single(entity => HasMetadata(entity, "v1:1:DIMENSION:002", "primary")),
            "v1:1:DIMENSION:002",
            "primary");
        AssertMetadata(
            reopened.Entities.OfType<Leader>().Single(entity => HasMetadata(entity, "v1:1:LEADER:003", "primary")),
            "v1:1:LEADER:003",
            "primary");
        AssertMetadata(
            reopened.Entities.OfType<TextEntity>().Single(entity => HasMetadata(entity, "v1:1:LEADER:003", "annotation")),
            "v1:1:LEADER:003",
            "annotation");
        AssertMetadata(
            reopened.Entities.OfType<Hatch>().Single(entity => HasMetadata(entity, "v1:1:HATCH:004", "primary")),
            "v1:1:HATCH:004",
            "primary");

        var inserts = reopened.Entities.OfType<Insert>()
            .Where(entity => entity.ExtendedData.TryGet(AppName, out _))
            .OrderBy(entity => entity.InsertPoint.Y)
            .ToArray();
        Assert.Equal(2, inserts.Length);

        AssertMetadata(inserts[0], "v1:1:LEVEL:005", "primary");
        Assert.Single(inserts[0].Attributes);
        AssertMetadata(inserts[0].Attributes.Single(), "v1:1:LEVEL:005", "attribute");

        AssertMetadata(inserts[1], "v1:1:LEVEL:006", "primary");
        Assert.Single(inserts[1].Attributes);
        AssertMetadata(inserts[1].Attributes.Single(), "v1:1:LEVEL:006", "attribute");
    }

    private static bool HasMetadata(Entity entity, string candidateId, string role)
    {
        if (!entity.ExtendedData.TryGet(AppName, out var data))
        {
            return false;
        }

        var values = data.Records.OfType<ExtendedDataString>()
            .Select(record => record.Value)
            .ToArray();
        return values.Length == 2
            && values[0] == candidateId
            && values[1] == role;
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
}
