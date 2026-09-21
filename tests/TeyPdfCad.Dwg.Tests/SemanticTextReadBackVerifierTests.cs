using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class SemanticTextReadBackVerifierTests
{
    [Fact]
    public void Dimension_text_change_rejects_candidate_even_when_measurement_and_geometry_match()
    {
        const string candidateId = "v1:1:DIMENSION:text";
        var drawing = new CadDocument();
        var dimension = new DimensionAligned(
            new XYZ(0, 0, 0),
            new XYZ(5200, 0, 0))
        {
            DefinitionPoint = new XYZ(0, 100, 0),
            Text = "2x<>"
        };
        CandidateMetadataCodec.Write(
            dimension,
            new(candidateId, "primary"));
        drawing.Entities.Add(dimension);

        var manifest = Manifest(
            candidateId,
            "DIMENSION",
            dimension,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["expectedMeasurement"] = dimension.Measurement.ToString(
                    "R",
                    System.Globalization.CultureInfo.InvariantCulture),
                ["measurementTolerance"] = "0.000001",
                ["expectedDimensionText"] = "2x<>"
            });

        // Preserve all structural geometry and CandidateId but corrupt only
        // the semantic annotation text.
        dimension.Text = "3x<>";

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains(
                "expectedDimensionText",
                StringComparison.Ordinal));
    }


    [Fact]
    public void Dimension_style_change_rejects_candidate_before_source_geometry_can_be_suppressed()
    {
        const string candidateId = "v1:1:DIMENSION:style";
        var drawing = new CadDocument();
        var style = new ACadSharp.Tables.DimensionStyle("TEY_DIM_STYLE")
        {
            LinearScaleFactor = 100d,
            TextHeight = 2.5d,
            ArrowSize = 2.5d,
            ExtensionLineOffset = 0.75d,
            ExtensionLineExtension = 1.25d,
            ScaleFactor = 1d,
            SuppressFirstDimensionLine = false,
            SuppressSecondDimensionLine = false,
            SuppressFirstExtensionLine = false,
            SuppressSecondExtensionLine = false,
            SuppressOutsideExtensions = false
        };
        drawing.DimensionStyles.Add(style);

        var dimension = new DimensionAligned(
            new XYZ(0, 0, 0),
            new XYZ(100, 0, 0))
        {
            DefinitionPoint = new XYZ(0, 10, 0),
            Style = style
        };
        CandidateMetadataCodec.Write(
            dimension,
            new(candidateId, "primary"));
        drawing.Entities.Add(dimension);

        var expectedStyleFingerprint =
            DwgEntityFingerprint.ComputeDimensionStyle(dimension);
        var manifest = Manifest(
            candidateId,
            "DIMENSION",
            dimension,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["expectedMeasurement"] = dimension.Measurement.ToString(
                    "R",
                    System.Globalization.CultureInfo.InvariantCulture),
                ["measurementTolerance"] = "0.000001",
                ["expectedDimensionText"] = string.Empty,
                ["dimensionStyleFingerprint"] = expectedStyleFingerprint
            });

        // Keep the dimension itself intact but make one required source
        // replacement component disappear visually.
        style.SuppressFirstExtensionLine = true;

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains(
                "dimensionStyleFingerprint",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Leader_annotation_must_match_expected_text_exactly()
    {
        const string candidateId = "v1:1:LEADER:text";
        var drawing = new CadDocument();

        var leader = new Leader
        {
            ArrowHeadEnabled = true,
            CreationType = LeaderCreationType.CreatedWithTextAnnotation,
            PathType = LeaderPathType.StraightLineSegments,
            TextHeight = 2.5
        };
        leader.Vertices.Add(new XYZ(0, 0, 0));
        leader.Vertices.Add(new XYZ(10, 10, 0));
        CandidateMetadataCodec.Write(
            leader,
            new(candidateId, "primary"));
        drawing.Entities.Add(leader);

        var annotation = new TextEntity
        {
            Value = "K-2",
            InsertPoint = new XYZ(10, 10, 0),
            Height = 2.5
        };
        CandidateMetadataCodec.Write(
            annotation,
            new(candidateId, "annotation"));
        drawing.Entities.Add(annotation);

        var manifest = new NativeWriteManifest(
            new Dictionary<string, ExpectedCandidate>(StringComparer.Ordinal)
            {
                [candidateId] = new(
                    candidateId,
                    "LEADER",
                    [
                        new ExpectedNativeEntity(
                            candidateId,
                            "primary",
                            "Leader",
                            DwgEntityFingerprint.ComputeGeometry(leader),
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["minimumVertices"] = "2"
                            }),
                        new ExpectedNativeEntity(
                            candidateId,
                            "annotation",
                            "Text",
                            DwgEntityFingerprint.ComputeGeometry(annotation),
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["minimumHeight"] = "2.5",
                                ["nonEmpty"] = "true",
                                ["expectedText"] = "K-1"
                            })
                    ])
            });

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains("expectedText", StringComparison.Ordinal));
    }

    [Fact]
    public void Level_attribute_must_match_expected_value_exactly()
    {
        const string candidateId = "v1:1:LEVEL:text";
        var drawing = new CadDocument();
        var block = new ACadSharp.Tables.BlockRecord("TEY_LEVEL_TEXT");
        block.Entities.Add(new AttributeDefinition
        {
            Tag = "LEVEL",
            Value = string.Empty,
            InsertPoint = new XYZ(0, 0, 0),
            Height = 2.5
        });
        drawing.BlockRecords.Add(block);

        var insert = new Insert(block);
        insert.Attributes.Single().Value = "+3.500";
        CandidateMetadataCodec.Write(
            insert,
            new(candidateId, "primary"));
        CandidateMetadataCodec.Write(
            insert.Attributes.Single(),
            new(candidateId, "attribute"));
        drawing.Entities.Add(insert);

        var manifest = new NativeWriteManifest(
            new Dictionary<string, ExpectedCandidate>(StringComparer.Ordinal)
            {
                [candidateId] = new(
                    candidateId,
                    "LEVEL",
                    [
                        new ExpectedNativeEntity(
                            candidateId,
                            "primary",
                            "Insert",
                            DwgEntityFingerprint.ComputeGeometry(insert),
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["blockName"] = "TEY_LEVEL_TEXT",
                                ["blockDefinitionFingerprint"] =
                                    DwgEntityFingerprint.ComputeBlockDefinition(insert)
                            }),
                        new ExpectedNativeEntity(
                            candidateId,
                            "attribute",
                            "AttributeEntity",
                            DwgEntityFingerprint.ComputeGeometry(
                                insert.Attributes.Single()),
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["attributeTag"] = "LEVEL",
                                ["nonEmptyValue"] = "true",
                                ["expectedAttributeValue"] = "+3.600"
                            })
                    ])
            });

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains(
                "expectedAttributeValue",
                StringComparison.Ordinal));
    }

    private static NativeWriteManifest Manifest(
        string candidateId,
        string semanticType,
        Dimension dimension,
        IReadOnlyDictionary<string, string> properties)
        => new(
            new Dictionary<string, ExpectedCandidate>(StringComparer.Ordinal)
            {
                [candidateId] = new(
                    candidateId,
                    semanticType,
                    [
                        new ExpectedNativeEntity(
                            candidateId,
                            "primary",
                            "Dimension",
                            DwgEntityFingerprint.ComputeGeometry(dimension),
                            properties)
                    ])
            });

    private static TemporaryDrawing WriteDrawing(CadDocument drawing)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "semantic-text.dwg");

        using (var output = File.Create(path))
        {
            var writer = new DwgWriter(output, drawing)
            {
                Configuration = new DwgWriterConfiguration
                {
                    CloseStream = false
                }
            };
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
