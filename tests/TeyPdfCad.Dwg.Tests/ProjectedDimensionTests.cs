using ACadSharp.Entities;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public class ProjectedDimensionTests
{
    [Fact]
    public void Horizontal_dimension_measures_projection_not_diagonal_between_extension_origins()
    {
        var source = new VectorPdfDocument([new VectorPdfPage(1, 720, 720, 0,
        [
            new VectorLine("dim", new(0, -5), new(30, -5), new VectorStyle()),
            new VectorLine("ext-a", new(0, -5), new(0, 20), new VectorStyle()),
            new VectorLine("ext-b", new(30, -5), new(30, 0), new VectorStyle()),
            new VectorText("text", "30", new(15, -5), 2.5, new VectorStyle())
        ])]);
        var candidate = new DimensionCandidate(DimensionKind.Rotated,
            new(0, 20), new(30, 0), new(15, -5), 30, 30, 1, 1, "30", 1,
            ["dim", "ext-a", "ext-b", "text"])
        {
            RotationRadians = 0,
            SourceClaims =
            [
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("ext-a", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("ext-b", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = new SemanticReconstructionResult([candidate], [], 1, 1);
        var bytes = new TeyPdfCad.Dwg.AcadSharpDwgWriter().Write(source,
            new DocumentLayoutPlanner().Create(source),
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult>{{1, semantics}});
        var doc = DwgReader.Read(new MemoryStream(bytes));
        var dimension = Assert.Single(doc.Entities.OfType<DimensionLinear>());
        Assert.Equal(30, dimension.Measurement, 6);
        Assert.Equal(0, dimension.Rotation, 6);
    }
}
