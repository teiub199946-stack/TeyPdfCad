using TeyPdfCad.TestGenerator.Generation;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class CoreRegressionPipelineTests
{
    [Fact]
    public async Task Clean_5200_At_1_100_RoundTrips_Through_Real_Core()
    {
        var testCase = CleanHorizontalCase();

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(1, actual.DetectedDimensions);
        Assert.Equal(5200, actual.Value!.Value, 3);
        Assert.Equal(100, actual.DrawingScale!.Value, 6);
        Assert.NotNull(actual.P1);
        Assert.NotNull(actual.P2);
        Assert.True(
            (actual.P1!.Value.DistanceTo(testCase.P1) < 0.1 && actual.P2!.Value.DistanceTo(testCase.P2) < 0.1)
            || (actual.P1!.Value.DistanceTo(testCase.P2) < 0.1 && actual.P2!.Value.DistanceTo(testCase.P1) < 0.1));
    }

    [Fact]
    public void SceneBuilder_Converts_Dwg_Coordinates_To_Paper_Coordinates()
    {
        var testCase = CleanHorizontalCase();

        var scene = new DimensionCasePrimitiveSceneBuilder().Build(testCase);

        Assert.Contains(scene.Lines, line => Math.Abs(line.Start.X) < 1e-9 && Math.Abs(line.End.X - 52.0) < 1e-6);
        var text = Assert.Single(scene.Texts);
        Assert.Equal("5200", text.Value);
        Assert.Equal(2.5, text.Height, 6);
        Assert.Equal(26.0, text.Position.X, 6);
        Assert.Equal(3.5, text.Position.Y, 6);
    }

    [Fact]
    public async Task RegressionRunner_RealCore_AcceptsGeometryInsideInjectedNoiseEnvelope()
    {
        var testCase = CleanHorizontalCase() with
        {
            Id = "core_noise_envelope",
            Noise = new NoiseSpec
            {
                CoordinateJitter = 0.2
            },
            ObservedGeometry = new ObservedGeometry
            {
                P1 = new Point2D(0.2, 0),
                P2 = new Point2D(5200.2, 0),
                DimensionLinePoint = new Point2D(2600.2, 350),
                TextPosition = new Point2D(2600.2, 350)
            }
        };
        var corpus = new TestCorpus
        {
            Seed = 12345,
            Cases = [testCase]
        };

        var report = await new RegressionRunner().RunAsync(
            corpus,
            new SemanticCoreTestPipeline(),
            new TestConfig());

        Assert.Equal(1, report.Passed);
        Assert.Equal(0, report.Failed);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task Mixed_short_chain_keeps_all_members_when_scale_hypotheses_compete()
    {
        var testCase = new DimensionCaseGenerator()
            .Generate(1226, 12345)
            .Cases
            .Single(testCase => testCase.Id == "case_001226");

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(DimensionType.Chain, actual.DimensionType);
        Assert.Equal(3, actual.DetectedDimensions);
        Assert.Equal(85d, actual.Value!.Value, 6);
        Assert.Equal(25d, actual.DrawingScale!.Value, 6);
    }

    [Fact]
    public async Task Short_canonical_dimension_survives_small_absolute_pdf_noise()
    {
        // TEST-002/003 case_005570: a 25 mm linear dimension at 1:50 is only
        // 0.5 mm long in paper space. 0.01 mm PDF/PDFIMPORT coordinate noise
        // pushes the raw inferred scale to ~48.6. That is inside the existing
        // 3% canonical-scale snap window for 1:50, but the old path then
        // rejected the same hypothesis against the independent 2% measurement
        // check. Keep both thresholds unchanged and require the semantic path to
        // handle this internally-consistent short-span case.
        var testCase = new DimensionCaseGenerator()
            .Generate(5_570, 12345)
            .Cases
            .Single(testCase => testCase.Id == "case_005570");

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(1, actual.DetectedDimensions);
        Assert.Equal(DimensionType.Linear, actual.DimensionType);
        Assert.Equal(25d, actual.Value!.Value, 6);
        Assert.Equal(50d, actual.DrawingScale!.Value, 6);
        Assert.NotNull(actual.P1);
        Assert.NotNull(actual.P2);

        var endpointError = Math.Min(
            actual.P1!.Value.DistanceTo(testCase.P1)
                + actual.P2!.Value.DistanceTo(testCase.P2),
            actual.P1!.Value.DistanceTo(testCase.P2)
                + actual.P2!.Value.DistanceTo(testCase.P1));
        Assert.True(endpointError <= 1.0);
    }

    [Fact]
    public async Task Borderline_ambiguous_case_uses_observable_one_sided_arrow_evidence_and_abstains()
    {
        var testCase = CleanHorizontalCase() with
        {
            Id = "core_ambiguous_one_sided_arrow",
            ExpectedResult = ExpectedResult.Ambiguous,
            ExpectedConfidenceClass = ConfidenceClass.Low,
            Tags = ["ambiguous", "borderline-evidence"]
        };

        var scene = new DimensionCasePrimitiveSceneBuilder().Build(testCase);
        var arrowSourceIds = scene.Lines
            .SelectMany(line => line.ProvenanceIds)
            .Where(id => id.Contains(":arrow:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(arrowSourceIds);
        Assert.All(arrowSourceIds, id =>
            Assert.Contains(":arrow:1", id, StringComparison.Ordinal));
        Assert.DoesNotContain(arrowSourceIds, id =>
            id.Contains(":arrow:2", StringComparison.Ordinal));

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Ambiguous, actual.Result);
        Assert.Equal(0, actual.DetectedDimensions);
        Assert.Equal(ConfidenceClass.Low, actual.ConfidenceClass);
        Assert.False(actual.SuppressionEvidenceEligible);
        Assert.Contains("AmbiguousDimensionEvidence", actual.SuppressionBlockers);
    }

    [Fact]
    public async Task LinesTextNoArrows_NegativePattern_Is_Not_Recognized()
    {
        var testCase = CleanHorizontalCase() with
        {
            Id = "core_negative_lines_text_no_arrows",
            DimensionType = DimensionType.Negative,
            ExpectedResult = ExpectedResult.Rejected,
            ExpectedDimensions = 0,
            ExpectedConfidenceClass = ConfidenceClass.None,
            NegativePattern = NegativePattern.LinesTextNoArrows
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Rejected, actual.Result);
        Assert.Equal(0, actual.DetectedDimensions);
        Assert.False(actual.SuppressionEvidenceEligible);
    }

    [Fact]
    public async Task NumberInsideBlock_NegativePattern_Is_Not_Recognized_Or_Suppressible()
    {
        var testCase = CleanHorizontalCase() with
        {
            Id = "core_negative_number_inside_block",
            DimensionType = DimensionType.Negative,
            ExpectedResult = ExpectedResult.Rejected,
            ExpectedDimensions = 0,
            ExpectedConfidenceClass = ConfidenceClass.None,
            NegativePattern = NegativePattern.NumberInsideBlock
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Rejected, actual.Result);
        Assert.Equal(0, actual.DetectedDimensions);
        Assert.False(actual.SuppressionEvidenceEligible);
        Assert.Contains("NoRecognizedDimension", actual.SuppressionBlockers);
    }

    [Theory]
    [InlineData(ArrowType.ClosedFilled)]
    [InlineData(ArrowType.ClosedBlank)]
    [InlineData(ArrowType.Open)]
    [InlineData(ArrowType.ArchitecturalTick)]
    [InlineData(ArrowType.Oblique)]
    [InlineData(ArrowType.Dot)]
    public async Task Supported_arrow_types_keep_positive_dimension_recognizable(
        ArrowType arrowType)
    {
        var testCase = CleanHorizontalCase() with
        {
            Id = "core_positive_arrow_" + arrowType,
            ArrowType = arrowType
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(1, actual.DetectedDimensions);
        Assert.True(actual.SuppressionEvidenceEligible);
    }

    [Fact]
    public async Task TextNearOrdinaryLine_NegativePattern_Is_Not_Recognized()
    {
        var testCase = CleanHorizontalCase() with
        {
            DimensionType = DimensionType.Negative,
            ExpectedResult = ExpectedResult.Rejected,
            ExpectedDimensions = 0,
            ExpectedConfidenceClass = ConfidenceClass.None,
            NegativePattern = NegativePattern.TextNearOrdinaryLine
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Rejected, actual.Result);
        Assert.Equal(0, actual.DetectedDimensions);
    }

    [Fact]
    public async Task NearAxis_Rotated_Candidate_Is_Not_Collapsed_To_Linear()
    {
        const double angleDegrees = 0.75;
        var radians = angleDegrees * Math.PI / 180.0;
        var direction = new Point2D(Math.Cos(radians), Math.Sin(radians));
        var normal = new Point2D(-direction.Y, direction.X);
        var p2 = new Point2D(direction.X * 5200.0, direction.Y * 5200.0);
        var midpoint = new Point2D(p2.X / 2.0, p2.Y / 2.0);
        var dimPoint = new Point2D(
            midpoint.X + normal.X * 350.0,
            midpoint.Y + normal.Y * 350.0);

        var testCase = CleanHorizontalCase() with
        {
            Id = "core_contract_rotated_near_axis",
            DimensionType = DimensionType.Rotated,
            P2 = p2,
            DimensionLinePoint = dimPoint,
            TextPosition = dimPoint,
            Rotation = angleDegrees,
            ObservedGeometry = new ObservedGeometry
            {
                P1 = new Point2D(0, 0),
                P2 = p2,
                DimensionLinePoint = dimPoint,
                TextPosition = dimPoint
            }
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(DimensionType.Rotated, actual.DimensionType);
        Assert.True(actual.IsDimensionTypeAmbiguous);
    }

    [Fact]
    public async Task NonAxis_Primitives_With_Parallel_Definition_And_Dimension_Directions_Are_Type_Ambiguous()
    {
        const double component = 3676.955262170047;
        var testCase = CleanHorizontalCase() with
        {
            Id = "core_contract_rotated_45",
            DimensionType = DimensionType.Rotated,
            P2 = new Point2D(component, component),
            DimensionLinePoint = new Point2D(1590.990243, 2085.965019),
            TextPosition = new Point2D(1590.990243, 2085.965019),
            Rotation = 45,
            ObservedGeometry = new ObservedGeometry
            {
                P1 = new Point2D(0, 0),
                P2 = new Point2D(component, component),
                DimensionLinePoint = new Point2D(1590.990243, 2085.965019),
                TextPosition = new Point2D(1590.990243, 2085.965019)
            }
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(DimensionType.Aligned, actual.DimensionType);
        Assert.True(actual.IsDimensionTypeAmbiguous);
    }

    private static DimensionCase CleanHorizontalCase()
    {
        return new DimensionCase
        {
            Id = "core_contract_5200",
            Seed = 12345,
            DimensionType = DimensionType.Linear,
            ExpectedValue = 5200,
            P1 = new Point2D(0, 0),
            P2 = new Point2D(5200, 0),
            DimensionLinePoint = new Point2D(2600, 350),
            Rotation = 0,
            TextPosition = new Point2D(2600, 350),
            TextPlacement = TextPlacement.Centered,
            TextHeight = 250,
            ArrowType = ArrowType.ClosedFilled,
            ArrowSize = 250,
            ExtensionLineOffset = 150,
            ExtensionLineExtension = 125,
            DrawingScale = 100,
            ExpectedConfidenceClass = ConfidenceClass.High,
            ExpectedResult = ExpectedResult.Recognized,
            ExpectedDimensions = 1,
            NegativePattern = NegativePattern.None,
            Noise = new NoiseSpec(),
            ObservedGeometry = new ObservedGeometry
            {
                P1 = new Point2D(0, 0),
                P2 = new Point2D(5200, 0),
                DimensionLinePoint = new Point2D(2600, 350),
                TextPosition = new Point2D(2600, 350)
            }
        };
    }
}
