using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class DiagnosticClassifierTests
{
    [Fact]
    public void RejectedExpected_ButDetected_IsUnexpectedDetection()
    {
        var expected = Case(ExpectedResult.Rejected) with { ExpectedDimensions = 0, DimensionType = DimensionType.Negative };
        var actual = Actual(expected) with { Result = ExpectedResult.Recognized, DetectedDimensions = 1 };
        Assert.Equal(DiagnosticCategory.UnexpectedDetection, Classify(expected, actual).Category);
    }

    [Fact]
    public void RecognizedExpected_ButRejected_IsMissedDetection()
    {
        var expected = Case(ExpectedResult.Recognized);
        var actual = Actual(expected) with { Result = ExpectedResult.Rejected, DetectedDimensions = 0 };
        Assert.Equal(DiagnosticCategory.MissedDetection, Classify(expected, actual).Category);
    }

    [Fact]
    public void ExpectedAmbiguous_AndAmbiguous_IsExpectedAbstention()
    {
        var expected = Case(ExpectedResult.Ambiguous);
        var actual = Actual(expected) with { Result = ExpectedResult.Ambiguous, DetectedDimensions = 0 };
        Assert.Equal(DiagnosticCategory.ExpectedAbstention, Classify(expected, actual).Category);
    }

    [Fact]
    public void ExpectedAmbiguous_ButForcedRecognition_IsWrongAbstention()
    {
        var expected = Case(ExpectedResult.Ambiguous);
        var actual = Actual(expected);
        Assert.Equal(DiagnosticCategory.WrongAbstention, Classify(expected, actual).Category);
    }

    [Fact]
    public void RecognizedExpected_ButActualAmbiguous_IsWrongAbstention()
    {
        var expected = Case(ExpectedResult.Recognized);
        var actual = Actual(expected) with { Result = ExpectedResult.Ambiguous, DetectedDimensions = 0 };
        Assert.Equal(DiagnosticCategory.WrongAbstention, Classify(expected, actual).Category);
    }

    [Fact]
    public void ChainCountMismatch_IsChainMismatch()
    {
        var expected = Case(ExpectedResult.Recognized) with
        {
            DimensionType = DimensionType.Chain,
            ExpectedDimensions = 5,
            Segments = Enumerable.Range(0, 5).Select(i => new DimensionSegment
            {
                ExpectedValue = 1000,
                P1 = new Point2D(i * 1000, 0),
                P2 = new Point2D((i + 1) * 1000, 0)
            }).ToList()
        };
        var actual = Actual(expected) with { DetectedDimensions = 3, DimensionType = DimensionType.Chain };
        Assert.Equal(DiagnosticCategory.ChainMismatch, Classify(expected, actual).Category);
    }

    [Fact]
    public void NonChainCountMismatch_IsWrongDimensionCount()
    {
        var expected = Case(ExpectedResult.Recognized);
        var actual = Actual(expected) with { DetectedDimensions = 2 };
        Assert.Equal(DiagnosticCategory.WrongDimensionCount, Classify(expected, actual).Category);
    }

    [Theory]
    [InlineData(5100, 100, DimensionType.Linear, DiagnosticCategory.WrongValue)]
    [InlineData(5200, 50, DimensionType.Linear, DiagnosticCategory.WrongScale)]
    [InlineData(5200, 100, DimensionType.Aligned, DiagnosticCategory.WrongDimensionType)]
    public void SemanticMismatch_PrecedesGeometry(double value, double scale, DimensionType type, DiagnosticCategory category)
    {
        var expected = Case(ExpectedResult.Recognized);
        var actual = Actual(expected) with { Value = value, DrawingScale = scale, DimensionType = type };
        Assert.Equal(category, Classify(expected, actual).Category);
    }

    [Theory]
    [InlineData(DimensionType.Rotated, DimensionType.Aligned)]
    [InlineData(DimensionType.Aligned, DimensionType.Rotated)]
    [InlineData(DimensionType.Aligned, DimensionType.Linear)]
    [InlineData(DimensionType.Rotated, DimensionType.Linear)]
    [InlineData(DimensionType.Linear, DimensionType.Aligned)]
    public void LinearFamilyVisualAmbiguity_DoesNotBecomeWrongType(
        DimensionType expectedType,
        DimensionType actualType)
    {
        var expected = Case(ExpectedResult.Recognized) with { DimensionType = expectedType };
        var actual = Actual(expected) with
        {
            DimensionType = actualType,
            IsDimensionTypeAmbiguous = true
        };

        Assert.NotEqual(
            DiagnosticCategory.WrongDimensionType,
            Classify(expected, actual).Category);
    }

    [Fact]
    public void EndpointSwap_IsGeometryEquivalent()
    {
        var expected = Case(ExpectedResult.Recognized);
        var actual = Actual(expected) with { P1 = expected.P2, P2 = expected.P1 };
        var trace = Trace(expected) with
        {
            CoreResult = Snapshot(expected, p1: expected.P2, p2: expected.P1)
        };

        var diagnostic = new ErrorClassifier().Classify(expected, actual, trace);
        Assert.NotEqual(DiagnosticCategory.WrongGeometry, diagnostic.Category);
        Assert.True(diagnostic.GeometryCorrect);
    }

    [Fact]
    public void InjectedPaperNoise_ExplainsPointDifference()
    {
        var expected = Case(ExpectedResult.Recognized) with
        {
            DrawingScale = 100,
            ObservedGeometry = new ObservedGeometry
            {
                P1 = new Point2D(0.02, 0),
                P2 = new Point2D(5200, 0),
                DimensionLinePoint = new Point2D(2600, 350),
                TextPosition = new Point2D(2600, 350)
            },
            Noise = new NoiseSpec { CoordinateJitter = 0.02 }
        };
        var actual = Actual(expected) with { P1 = new Point2D(2, 0) };
        var trace = Trace(expected) with
        {
            DefinitionPoint1 = new GeometryPointTrace
            {
                ExpectedWorld = expected.P1,
                CleanPaper = new Point2D(0, 0),
                CoreInputPaper = new Point2D(0.02, 0),
                InjectedPaperDelta = new Point2D(0.02, 0)
            },
            CoreResult = Snapshot(expected, p1: new Point2D(2, 0), p2: expected.P2)
        };

        var diagnostic = new ErrorClassifier().Classify(expected, actual, trace);
        Assert.Equal(DiagnosticCategory.ExpectedNoisePropagation, diagnostic.Category);
        Assert.True(diagnostic.WasLegacyWrongPoints);
        Assert.False(diagnostic.IsRealCoreDefect);
    }

    [Fact]
    public void ErrorBeyondNoiseEnvelope_IsWrongGeometry()
    {
        var expected = Case(ExpectedResult.Recognized) with { DrawingScale = 100 };
        var actual = Actual(expected) with { P1 = new Point2D(10, 0) };
        var trace = Trace(expected) with
        {
            CoreResult = Snapshot(expected, p1: new Point2D(10, 0), p2: expected.P2)
        };

        var diagnostic = new ErrorClassifier().Classify(expected, actual, trace);
        Assert.Equal(DiagnosticCategory.WrongGeometry, diagnostic.Category);
        Assert.True(diagnostic.IsRealCoreDefect);
    }

    [Fact]
    public void DimensionLinePoint_AlongLineTranslation_IsGeometryEquivalent()
    {
        var expected = Case(ExpectedResult.Recognized) with { DrawingScale = 100, Rotation = 0 };
        var shiftedWorld = new Point2D(expected.DimensionLinePoint.X + 1000, expected.DimensionLinePoint.Y);
        var shiftedPaper = new Point2D(
            shiftedWorld.X / expected.DrawingScale,
            shiftedWorld.Y / expected.DrawingScale);
        var trace = Trace(expected) with
        {
            CoreResult = Snapshot(expected, expected.P1, expected.P2) with
            {
                DimensionLinePointWorld = shiftedWorld,
                DimensionLinePointPaper = shiftedPaper
            }
        };

        var diagnostic = new ErrorClassifier().Classify(expected, Actual(expected), trace);

        Assert.NotEqual(DiagnosticCategory.WrongGeometry, diagnostic.Category);
        Assert.True(diagnostic.GeometryCorrect);
    }

    [Fact]
    public void DimensionLinePoint_PerpendicularTranslation_RemainsWrongGeometry()
    {
        var expected = Case(ExpectedResult.Recognized) with { DrawingScale = 100, Rotation = 0 };
        var shiftedWorld = new Point2D(expected.DimensionLinePoint.X, expected.DimensionLinePoint.Y + 10);
        var shiftedPaper = new Point2D(
            shiftedWorld.X / expected.DrawingScale,
            shiftedWorld.Y / expected.DrawingScale);
        var trace = Trace(expected) with
        {
            CoreResult = Snapshot(expected, expected.P1, expected.P2) with
            {
                DimensionLinePointWorld = shiftedWorld,
                DimensionLinePointPaper = shiftedPaper
            }
        };

        var diagnostic = new ErrorClassifier().Classify(expected, Actual(expected), trace);

        Assert.Equal(DiagnosticCategory.WrongGeometry, diagnostic.Category);
        Assert.True(diagnostic.IsRealCoreDefect);
    }

    private static CaseDiagnostic Classify(DimensionCase expected, ActualDimensionResult actual)
        => new ErrorClassifier().Classify(expected, actual, Trace(expected));

    private static DimensionCase Case(ExpectedResult result) => new()
    {
        Id = "classifier_case",
        Seed = 12345,
        DimensionType = DimensionType.Linear,
        ExpectedValue = 5200,
        P1 = new Point2D(0, 0),
        P2 = new Point2D(5200, 0),
        DimensionLinePoint = new Point2D(2600, 350),
        Rotation = 0,
        TextPosition = new Point2D(2600, 350),
        DrawingScale = 100,
        ExpectedResult = result,
        ExpectedDimensions = result == ExpectedResult.Recognized ? 1 : 0,
        ExpectedConfidenceClass = result == ExpectedResult.Recognized ? ConfidenceClass.High : ConfidenceClass.None,
        Noise = new NoiseSpec(),
        ObservedGeometry = new ObservedGeometry
        {
            P1 = new Point2D(0, 0),
            P2 = new Point2D(5200, 0),
            DimensionLinePoint = new Point2D(2600, 350),
            TextPosition = new Point2D(2600, 350)
        }
    };

    private static ActualDimensionResult Actual(DimensionCase expected) => new()
    {
        CaseId = expected.Id,
        Result = ExpectedResult.Recognized,
        DetectedDimensions = expected.ExpectedDimensions == 0 ? 1 : expected.ExpectedDimensions,
        Value = expected.ExpectedValue,
        P1 = expected.P1,
        P2 = expected.P2,
        DimensionLinePoint = expected.DimensionLinePoint,
        DimensionType = expected.DimensionType,
        DrawingScale = expected.DrawingScale,
        ConfidenceClass = expected.ExpectedConfidenceClass
    };

    private static CaseGeometryTrace Trace(DimensionCase expected) => new()
    {
        CaseId = expected.Id,
        DrawingScale = expected.DrawingScale,
        InjectedNoise = expected.Noise,
        DefinitionPoint1 = PointTrace(expected.P1, expected.DrawingScale),
        DefinitionPoint2 = PointTrace(expected.P2, expected.DrawingScale),
        DimensionLineLocation = PointTrace(expected.DimensionLinePoint, expected.DrawingScale),
        TextAnchor = PointTrace(expected.TextPosition, expected.DrawingScale),
        ExpectedRotationDegrees = expected.Rotation,
        CoreResult = Snapshot(expected, expected.P1, expected.P2)
    };

    private static GeometryPointTrace PointTrace(Point2D world, double scale) => new()
    {
        ExpectedWorld = world,
        CleanPaper = new Point2D(world.X / scale, world.Y / scale),
        CoreInputPaper = new Point2D(world.X / scale, world.Y / scale),
        InjectedPaperDelta = new Point2D(0, 0)
    };

    private static CoreGeometrySnapshot Snapshot(DimensionCase expected, Point2D p1, Point2D p2) => new()
    {
        DefinitionPoint1World = p1,
        DefinitionPoint2World = p2,
        DimensionLinePointWorld = expected.DimensionLinePoint,
        DefinitionPoint1Paper = new Point2D(p1.X / expected.DrawingScale, p1.Y / expected.DrawingScale),
        DefinitionPoint2Paper = new Point2D(p2.X / expected.DrawingScale, p2.Y / expected.DrawingScale),
        DimensionLinePointPaper = new Point2D(expected.DimensionLinePoint.X / expected.DrawingScale, expected.DimensionLinePoint.Y / expected.DrawingScale),
        ProvenanceIds = new List<string> { expected.Id + ":primary:dimline", expected.Id + ":primary:ext:1", expected.Id + ":primary:ext:2" }
    };
}
