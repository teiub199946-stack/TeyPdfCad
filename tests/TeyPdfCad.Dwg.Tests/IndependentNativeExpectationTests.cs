using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class IndependentNativeExpectationTests
{
    [Fact]
    public void Wrong_axis_geometry_is_rejected_against_prewrite_manifest()
    {
        var page = new VectorPdfPage(1, 240, 100, 0,
        [
            new VectorLine("axis", new(10, 40), new(180, 40), new VectorStyle())
        ]);
        var candidate = new AxisCandidate(
            new(10, 40),
            new(180, 40),
            0.95,
            ["axis"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with { Axes = [candidate] };
        var hatch = new HatchRecognitionResult([], []);
        var plan = new SourceReplacementPlanner().BuildPlan(
            page.Entities, semantics, hatch, page.Number);
        var document = new VectorPdfDocument([page]);
        var layout = new DocumentLayoutPlanner().Create(document);

        var manifest = NativeExpectationBuilder.Build(
            document,
            layout,
            new Dictionary<int, HatchRecognitionResult> { [1] = hatch },
            new Dictionary<int, SemanticReconstructionResult> { [1] = semantics },
            new Dictionary<int, SourceReplacementPlan> { [1] = plan });

        var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, 1);
        var expected = manifest.Candidates[candidateId];
        Assert.False(expected.SourceEquivalenceComplete);

        var drawing = new CadDocument();
        var block = new BlockRecord("TEY_AXIS");
        block.Entities.Add(new Line(
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0)));
        drawing.BlockRecords.Add(block);

        // Intentional writer defect: wrong length and wrong insertion point.
        var wrong = new Insert(block)
        {
            InsertPoint = new XYZ(15, 40, 0),
            XScale = 120,
            YScale = 1,
            ZScale = 1,
            Rotation = 0
        };
        CandidateMetadataCodec.Write(
            wrong,
            new CandidateEntityMetadata(candidateId, "primary"));
        drawing.Entities.Add(wrong);

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.False(verification.SourceEquivalenceComplete);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains(
                "geometry fingerprint mismatch",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Wrong_dimension_definition_point_is_rejected_against_prewrite_manifest()
    {
        var page = new VectorPdfPage(1, 100, 100, 0,
        [
            new VectorLine("dim", new(10, 10), new(60, 10), new VectorStyle()),
            new VectorLine("ext", new(10, 10), new(10, 20), new VectorStyle()),
            new VectorLine("arrow", new(10, 20), new(12, 22), new VectorStyle()),
            new VectorText("text", "50", new(35, 20), 2.5, new VectorStyle())
        ]);
        var candidate = new DimensionCandidate(
            DimensionKind.Aligned,
            new(10, 10),
            new(60, 10),
            new(35, 20),
            50,
            50,
            1,
            0.95,
            "50",
            1,
            ["dim", "ext", "arrow", "text"])
        {
            SourceClaims =
            [
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("ext", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("arrow", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };
        var hatch = new HatchRecognitionResult([], []);
        var plan = new SourceReplacementPlanner().BuildPlan(
            page.Entities, semantics, hatch, page.Number);
        var document = new VectorPdfDocument([page]);
        var layout = new DocumentLayoutPlanner().Create(document);
        var manifest = NativeExpectationBuilder.Build(
            document,
            layout,
            new Dictionary<int, HatchRecognitionResult> { [1] = hatch },
            new Dictionary<int, SemanticReconstructionResult> { [1] = semantics },
            new Dictionary<int, SourceReplacementPlan> { [1] = plan });

        var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, 1);
        var expected = manifest.Candidates[candidateId];
        Assert.False(expected.SourceEquivalenceComplete);

        var drawing = new CadDocument();
        var style = new DimensionStyle("TEYPDFCAD_SCALE_1")
        {
            LinearScaleFactor = 1,
            TextHeight = 2.5,
            ArrowSize = 2.5,
            ExtensionLineOffset = 0.75,
            ExtensionLineExtension = 1.25,
            ScaleFactor = 1
        };
        drawing.DimensionStyles.Add(style);

        // Intentional writer defect: source/candidate expects dimension line at Y=20.
        var wrong = new DimensionAligned(
            new XYZ(10, 10, 0),
            new XYZ(60, 10, 0))
        {
            DefinitionPoint = new XYZ(35, 30, 0),
            Style = style,
            Text = string.Empty
        };
        CandidateMetadataCodec.Write(
            wrong,
            new CandidateEntityMetadata(candidateId, "primary"));
        drawing.Entities.Add(wrong);

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains(
                "geometry fingerprint mismatch",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Wrong_dimension_layer_is_rejected_against_prewrite_manifest()
    {
        var page = new VectorPdfPage(1, 100, 100, 0,
        [
            new VectorLine("dim", new(10, 20), new(60, 20), new VectorStyle()),
            new VectorLine("ext-1", new(10, 10), new(10, 21), new VectorStyle()),
            new VectorLine("ext-2", new(60, 10), new(60, 21), new VectorStyle()),
            new VectorLine("arrow-1", new(10, 20), new(12, 22), new VectorStyle()),
            new VectorLine("arrow-2", new(60, 20), new(58, 22), new VectorStyle()),
            new VectorText("text", "50", new(35, 20), 2.5, new VectorStyle())
        ]);
        var candidate = new DimensionCandidate(
            DimensionKind.Aligned,
            new(10, 10),
            new(60, 10),
            new(35, 20),
            50,
            50,
            1,
            0.95,
            "50",
            1,
            ["dim", "ext-1", "ext-2", "arrow-1", "arrow-2", "text"])
        {
            SourceClaims =
            [
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("ext-1", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("ext-2", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("arrow-1", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("arrow-2", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };
        var hatch = new HatchRecognitionResult([], []);
        var plan = new SourceReplacementPlanner().BuildPlan(
            page.Entities, semantics, hatch, page.Number);
        var document = new VectorPdfDocument([page]);
        var layout = new DocumentLayoutPlanner().Create(document);
        var manifest = NativeExpectationBuilder.Build(
            document,
            layout,
            new Dictionary<int, HatchRecognitionResult> { [1] = hatch },
            new Dictionary<int, SemanticReconstructionResult> { [1] = semantics },
            new Dictionary<int, SourceReplacementPlan> { [1] = plan });

        var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, 1);
        var drawing = new CadDocument();
        var style = new DimensionStyle("TEYPDFCAD_SCALE_1")
        {
            LinearScaleFactor = 1,
            TextHeight = 2.5,
            ArrowSize = 2.5,
            ExtensionLineOffset = 0.75,
            ExtensionLineExtension = 1.25,
            ScaleFactor = 1
        };
        drawing.DimensionStyles.Add(style);

        var wrong = new DimensionAligned(
            new XYZ(10, 10, 0),
            new XYZ(60, 10, 0))
        {
            DefinitionPoint = new XYZ(35, 20, 0),
            Style = style,
            Text = string.Empty
            // Intentional writer defect: stays on the default layer instead of PDF_РАЗМЕРЫ.
        };
        CandidateMetadataCodec.Write(
            wrong,
            new CandidateEntityMetadata(candidateId, "primary"));
        drawing.Entities.Add(wrong);

        using var file = WriteDrawing(drawing);
        var verification = new DwgReadBackVerifier()
            .Verify(file.Path, manifest)
            .Candidates[candidateId];

        Assert.False(verification.IsVerified);
        Assert.Contains(
            verification.InvalidEntities,
            value => value.Contains("layer", StringComparison.OrdinalIgnoreCase));
    }

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);

    private static TemporaryDrawing WriteDrawing(CadDocument drawing)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "wrong-native.dwg");

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
