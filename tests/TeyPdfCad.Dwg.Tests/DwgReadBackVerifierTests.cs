using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DwgReadBackVerifierTests
{
    [Fact]
    public void Missing_leader_annotation_rejects_candidate()
    {
        const string candidateId = "v1:1:LEADER:001";
        var drawing = new CadDocument();
        var leader = NewLeader();
        CandidateMetadataCodec.Write(leader, new(candidateId, "primary"));
        drawing.Entities.Add(leader);

        var manifest = Manifest(candidateId, "LEADER",
            Expected(candidateId, "primary", "Leader", leader),
            new ExpectedNativeEntity(
                candidateId,
                "annotation",
                "Text",
                "unused-because-role-is-missing",
                Props(("minimumHeight", "1"), ("nonEmpty", "true"))));

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier().Verify(file.Path, manifest);
        var candidate = verification.Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains("annotation", candidate.MissingRoles);
    }

    [Fact]
    public void Wrong_entity_type_with_right_role_rejects_candidate()
    {
        const string candidateId = "v1:1:DIMENSION:002";
        var drawing = new CadDocument();
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        CandidateMetadataCodec.Write(line, new(candidateId, "primary"));
        drawing.Entities.Add(line);

        var manifest = Manifest(candidateId, "DIMENSION",
            new ExpectedNativeEntity(
                candidateId,
                "primary",
                "Dimension",
                DwgEntityFingerprint.ComputeGeometry(line),
                Props()));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains(candidate.InvalidEntities,
            value => value.Contains("type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_primary_role_rejects_candidate()
    {
        const string candidateId = "v1:1:LINE:003";
        var drawing = new CadDocument();
        var first = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var second = new Line(new XYZ(0, 1, 0), new XYZ(10, 1, 0));
        CandidateMetadataCodec.Write(first, new(candidateId, "primary"));
        CandidateMetadataCodec.Write(second, new(candidateId, "primary"));
        drawing.Entities.Add(first);
        drawing.Entities.Add(second);

        var manifest = Manifest(candidateId, "LINE",
            Expected(candidateId, "primary", "Line", first));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains("primary", candidate.DuplicateRoles);
    }

    [Fact]
    public void Level_attribute_must_remain_child_of_expected_insert()
    {
        const string candidateId = "v1:1:LEVEL:004";
        const string otherCandidate = "v1:1:LEVEL:other";
        var drawing = new CadDocument();
        var block = NewLevelBlock("LEVEL_TEST");
        drawing.BlockRecords.Add(block);

        var expectedInsert = new Insert(block) { InsertPoint = new XYZ(0, 0, 0) };
        expectedInsert.Attributes.Single().Value = "+0.000";
        CandidateMetadataCodec.Write(expectedInsert, new(candidateId, "primary"));
        drawing.Entities.Add(expectedInsert);

        var wrongParent = new Insert(block) { InsertPoint = new XYZ(20, 0, 0) };
        wrongParent.Attributes.Single().Value = "+0.000";
        CandidateMetadataCodec.Write(wrongParent, new(otherCandidate, "primary"));
        CandidateMetadataCodec.Write(wrongParent.Attributes.Single(), new(candidateId, "attribute"));
        drawing.Entities.Add(wrongParent);

        var manifest = Manifest(candidateId, "LEVEL",
            Expected(candidateId, "primary", "Insert", expectedInsert,
                ("blockName", "LEVEL_TEST"),
                ("minimumScale", "0.5")),
            Expected(candidateId, "attribute", "AttributeEntity", wrongParent.Attributes.Single(),
                ("attributeTag", "LEVEL"),
                ("nonEmptyValue", "true")));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains(candidate.InvalidEntities,
            value => value.Contains("parent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Required_properties_fail_closed_for_dimension_leader_text_axis_level_and_hatch()
    {
        var drawing = new CadDocument();

        const string dimensionId = "v1:1:DIMENSION:101";
        var dimension = new DimensionAligned(new XYZ(0, 0, 0), new XYZ(10, 0, 0))
        {
            DefinitionPoint = new XYZ(0, 5, 0)
        };
        CandidateMetadataCodec.Write(dimension, new(dimensionId, "primary"));
        drawing.Entities.Add(dimension);

        const string leaderId = "v1:1:LEADER:102";
        var leader = new Leader
        {
            ArrowHeadEnabled = true,
            CreationType = LeaderCreationType.CreatedWithoutAnnotation,
            PathType = LeaderPathType.StraightLineSegments,
            TextHeight = 2.5
        };
        leader.Vertices.Add(new XYZ(0, 20, 0));
        CandidateMetadataCodec.Write(leader, new(leaderId, "primary"));
        drawing.Entities.Add(leader);

        const string textId = "v1:1:TEXT:103";
        var text = new TextEntity
        {
            Value = string.Empty,
            InsertPoint = new XYZ(0, 30, 0),
            Height = 2.5
        };
        CandidateMetadataCodec.Write(text, new(textId, "primary"));
        drawing.Entities.Add(text);

        const string axisId = "v1:1:AXIS:104";
        var axisBlock = new BlockRecord("AXIS_TEST");
        axisBlock.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
        drawing.BlockRecords.Add(axisBlock);
        var axis = new Insert(axisBlock)
        {
            InsertPoint = new XYZ(0, 40, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1
        };
        CandidateMetadataCodec.Write(axis, new(axisId, "primary"));
        drawing.Entities.Add(axis);

        const string levelId = "v1:1:LEVEL:105";
        var levelBlock = NewLevelBlock("LEVEL_PROP_TEST");
        drawing.BlockRecords.Add(levelBlock);
        var level = new Insert(levelBlock) { InsertPoint = new XYZ(0, 50, 0) };
        level.Attributes.Single().Value = string.Empty;
        CandidateMetadataCodec.Write(level, new(levelId, "primary"));
        CandidateMetadataCodec.Write(level.Attributes.Single(), new(levelId, "attribute"));
        drawing.Entities.Add(level);

        const string hatchId = "v1:1:HATCH:106";
        var hatchBoundary = new LwPolyline(
            [new XY(0, 60), new XY(10, 60), new XY(10, 70), new XY(0, 70)])
        {
            IsClosed = true
        };
        drawing.Entities.Add(hatchBoundary);
        var hatch = new Hatch
        {
            IsSolid = true,
            Pattern = HatchPattern.Solid,
            SeedPoints = [new XY(5, 65)]
        };
        hatch.Paths.Add(new Hatch.BoundaryPath([hatchBoundary]));
        CandidateMetadataCodec.Write(hatch, new(hatchId, "primary"));
        drawing.Entities.Add(hatch);

        var manifest = new NativeWriteManifest(
            new Dictionary<string, ExpectedCandidate>(StringComparer.Ordinal)
            {
                [dimensionId] = new(dimensionId, "DIMENSION",
                [
                    Expected(dimensionId, "primary", "Dimension", dimension,
                        ("expectedMeasurement", "99"),
                        ("measurementTolerance", "0.001"))
                ]),
                [leaderId] = new(leaderId, "LEADER",
                [
                    Expected(leaderId, "primary", "Leader", leader,
                        ("minimumVertices", "2"))
                ]),
                [textId] = new(textId, "TEXT",
                [
                    Expected(textId, "primary", "Text", text,
                        ("minimumHeight", "3"),
                        ("nonEmpty", "true"))
                ]),
                [axisId] = new(axisId, "AXIS",
                [
                    Expected(axisId, "primary", "Insert", axis,
                        ("blockName", "WRONG_BLOCK"),
                        ("minimumScale", "2"),
                        ("minimumLength", "2"))
                ]),
                [levelId] = new(levelId, "LEVEL",
                [
                    Expected(levelId, "primary", "Insert", level,
                        ("blockName", "LEVEL_PROP_TEST")),
                    Expected(levelId, "attribute", "AttributeEntity", level.Attributes.Single(),
                        ("attributeTag", "LEVEL"),
                        ("nonEmptyValue", "true"))
                ]),
                [hatchId] = new(hatchId, "HATCH",
                [
                    Expected(hatchId, "primary", "Hatch", hatch,
                        ("minimumArea", "200"),
                        ("boundaryFingerprint", "definitely-wrong"))
                ])
            });

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier().Verify(file.Path, manifest);

        foreach (var id in manifest.Candidates.Keys)
        {
            Assert.False(verification.Candidates[id].IsVerified);
            Assert.NotEmpty(verification.Candidates[id].InvalidEntities);
        }
    }


    [Fact]
    public void Block_definition_change_rejects_insert_candidate()
    {
        const string candidateId = "v1:1:AXIS:block-definition";
        var drawing = new CadDocument();
        var block = new BlockRecord("TEY_AXIS");
        block.Entities.Add(new Line(
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0)));
        drawing.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(10, 10, 0),
            XScale = 25,
            YScale = 1,
            ZScale = 1
        };
        CandidateMetadataCodec.Write(
            insert,
            new(candidateId, "primary"));
        drawing.Entities.Add(insert);

        var expectedBlockFingerprint =
            DwgEntityFingerprint.ComputeBlockDefinition(insert);
        var manifest = Manifest(
            candidateId,
            "AXIS",
            Expected(
                candidateId,
                "primary",
                "Insert",
                insert,
                ("blockName", "TEY_AXIS"),
                ("blockDefinitionFingerprint", expectedBlockFingerprint),
                ("minimumScale", "1"),
                ("minimumLength", "1")));

        // Simulate a writer/serialization defect that changes visible block
        // contents while preserving INSERT identity and candidate metadata.
        block.Entities.Add(new Line(
            new XYZ(0, 5, 0),
            new XYZ(1, 5, 0)));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains(
            candidate.InvalidEntities,
            value => value.Contains(
                "block definition fingerprint mismatch",
                StringComparison.Ordinal));
    }

    [Fact]
    public void BlockRecord_candidate_metadata_rejects_verification()
    {
        const string candidateId = "v1:1:AXIS:201";
        var drawing = new CadDocument();
        var block = new BlockRecord("AXIS_BLOCK");
        block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
        block.ExtendedData.Add(CandidateMetadataCodec.AppId, new ExtendedData(
        [
            new ExtendedDataString(candidateId),
            new ExtendedDataString("primary")
        ]));
        drawing.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            XScale = 10,
            YScale = 1,
            ZScale = 1
        };
        CandidateMetadataCodec.Write(insert, new(candidateId, "primary"));
        drawing.Entities.Add(insert);

        var manifest = Manifest(candidateId, "AXIS",
            Expected(candidateId, "primary", "Insert", insert,
                ("blockName", "AXIS_BLOCK"),
                ("minimumScale", "1"),
                ("minimumLength", "1")));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains(candidate.InvalidEntities,
            value => value.Contains("BlockRecord", StringComparison.Ordinal));
    }

    [Fact]
    public void Structural_inventory_counts_modelspace_and_nested_attributes_not_block_definitions()
    {
        var drawing = new CadDocument();
        var block = NewLevelBlock("LEVEL_INV");
        drawing.BlockRecords.Add(block);
        var insert = new Insert(block);
        insert.Attributes.Single().Value = "+1.000";
        CandidateMetadataCodec.Write(insert, new("candidate", "primary"));
        CandidateMetadataCodec.Write(insert.Attributes.Single(), new("candidate", "attribute"));
        drawing.Entities.Add(insert);
        drawing.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));

        using var file = WriteDrawing(drawing);
        var inventory = new DwgReadBackVerifier().ReadStructuralInventory(file.Path);

        Assert.Equal(3, inventory.CountedEntityCount);
        Assert.Equal(2, inventory.CandidateMetadataEntityCount);
        Assert.Equal(3, inventory.OutputFingerprintCounts.Values.Sum());
    }


    [Fact]
    public void Unexpected_candidate_id_in_dwg_rejects_expected_manifest_candidate()
    {
        const string expectedId = "v1:1:LINE:expected";
        const string unexpectedId = "v1:1:LINE:unexpected";
        var drawing = new CadDocument();

        var expectedLine = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        CandidateMetadataCodec.Write(expectedLine, new(expectedId, "primary"));
        drawing.Entities.Add(expectedLine);

        var unexpectedLine = new Line(new XYZ(0, 1, 0), new XYZ(10, 1, 0));
        CandidateMetadataCodec.Write(unexpectedLine, new(unexpectedId, "primary"));
        drawing.Entities.Add(unexpectedLine);

        var manifest = Manifest(
            expectedId,
            "LINE",
            Expected(expectedId, "primary", "Line", expectedLine));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[expectedId];

        Assert.False(candidate.IsVerified);
        Assert.Contains(candidate.InvalidEntities,
            value => value.Contains("Unexpected CandidateId", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_role_declared_by_manifest_is_rejected()
    {
        const string candidateId = "v1:1:LINE:dup-role";
        var drawing = new CadDocument();
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        CandidateMetadataCodec.Write(line, new(candidateId, "primary"));
        drawing.Entities.Add(line);

        var expectedEntity = Expected(candidateId, "primary", "Line", line);
        var manifest = Manifest(
            candidateId,
            "LINE",
            expectedEntity,
            expectedEntity);

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(candidate.IsVerified);
        Assert.Contains("primary", candidate.DuplicateRoles);
    }

    [Fact]
    public void Correct_line_candidate_verifies_from_closed_file()
    {
        const string candidateId = "v1:1:LINE:ok";
        var drawing = new CadDocument();
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        CandidateMetadataCodec.Write(line, new(candidateId, "primary"));
        drawing.Entities.Add(line);

        var manifest = Manifest(
            candidateId,
            "LINE",
            Expected(candidateId, "primary", "Line", line));

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.True(candidate.IsVerified);
        Assert.Empty(candidate.MissingRoles);
        Assert.Empty(candidate.DuplicateRoles);
        Assert.Empty(candidate.InvalidEntities);
    }

    [Fact]
    public void Dimension_measurement_uses_manifest_tolerance()
    {
        const string candidateId = "v1:1:DIMENSION:tolerance";
        var drawing = new CadDocument();
        var dimension = new DimensionAligned(
            new XYZ(0, 0, 0),
            new XYZ(10, 0, 0))
        {
            DefinitionPoint = new XYZ(0, 5, 0)
        };
        CandidateMetadataCodec.Write(dimension, new(candidateId, "primary"));
        drawing.Entities.Add(dimension);

        var expected = Expected(
            candidateId,
            "primary",
            "Dimension",
            dimension,
            ("expectedMeasurement", "10.0005"),
            ("measurementTolerance", "0.001"));
        var manifest = Manifest(candidateId, "DIMENSION", expected);

        using var file = WriteDrawing(drawing);
        var candidate = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.True(candidate.IsVerified);
    }

    private static ExpectedNativeEntity Expected(
        string candidateId,
        string role,
        string entityKind,
        Entity entity,
        params (string Key, string Value)[] properties)
        => new(
            candidateId,
            role,
            entityKind,
            DwgEntityFingerprint.ComputeGeometry(entity),
            Props(properties));

    private static NativeWriteManifest Manifest(
        string candidateId,
        string semanticType,
        params ExpectedNativeEntity[] entities)
        => new(new Dictionary<string, ExpectedCandidate>(StringComparer.Ordinal)
        {
            [candidateId] = new(candidateId, semanticType, entities)
        });

    private static IReadOnlyDictionary<string, string> Props(
        params (string Key, string Value)[] values)
        => values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

    private static Leader NewLeader()
    {
        var leader = new Leader
        {
            ArrowHeadEnabled = true,
            CreationType = LeaderCreationType.CreatedWithTextAnnotation,
            PathType = LeaderPathType.StraightLineSegments,
            TextHeight = 2.5
        };
        leader.Vertices.Add(new XYZ(0, 0, 0));
        leader.Vertices.Add(new XYZ(10, 10, 0));
        return leader;
    }

    private static BlockRecord NewLevelBlock(string name)
    {
        var block = new BlockRecord(name);
        block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(5, 0, 0)));
        block.Entities.Add(new AttributeDefinition
        {
            Tag = "LEVEL",
            Value = string.Empty,
            InsertPoint = new XYZ(6, 0, 0),
            Height = 2.5
        });
        return block;
    }

    private static TemporaryDrawing WriteDrawing(CadDocument drawing)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "probe.dwg");

        using (var output = File.Create(path))
        using (var writer = new DwgWriter(output, drawing))
        {
            writer.Write();
        }

        return new TemporaryDrawing(path, directory);
    }

    private sealed class TemporaryDrawing : IDisposable
    {
        public string Path { get; }
        private readonly string _directory;

        public TemporaryDrawing(string path, string directory)
        {
            Path = path;
            _directory = directory;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
