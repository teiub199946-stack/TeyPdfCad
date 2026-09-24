using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class DiagnosticFlagPropagationTests
{
    [Fact]
    public void GeometryClassification_MarksAllPriorSemanticDimensionsEvaluated()
    {
        var expected = Case();
        var actual = Actual(expected);
        var diagnostic = new ErrorClassifier().Classify(expected, actual, Trace(expected));

        Assert.True(diagnostic.CountCorrect);
        Assert.True(diagnostic.ValueCorrect);
        Assert.True(diagnostic.ScaleCorrect);
        Assert.True(diagnostic.TypeCorrect);
        Assert.True(diagnostic.GeometryEvaluated);
    }

    [Fact]
    public void WrongScale_PreservesSuccessfulCountAndValueDenominators()
    {
        var expected = Case();
        var actual = Actual(expected) with { DrawingScale = 50 };
        var diagnostic = new ErrorClassifier().Classify(expected, actual, Trace(expected));

        Assert.Equal(DiagnosticCategory.WrongScale, diagnostic.Category);
        Assert.True(diagnostic.CountCorrect);
        Assert.True(diagnostic.ValueCorrect);
        Assert.False(diagnostic.ScaleCorrect);
        Assert.Null(diagnostic.TypeCorrect);
        Assert.False(diagnostic.GeometryEvaluated);
    }

    [Fact]
    public void WrongType_PreservesSuccessfulCountValueAndScaleDenominators()
    {
        var expected = Case();
        var actual = Actual(expected) with { DimensionType = DimensionType.Aligned };
        var diagnostic = new ErrorClassifier().Classify(expected, actual, Trace(expected));

        Assert.Equal(DiagnosticCategory.WrongDimensionType, diagnostic.Category);
        Assert.True(diagnostic.CountCorrect);
        Assert.True(diagnostic.ValueCorrect);
        Assert.True(diagnostic.ScaleCorrect);
        Assert.False(diagnostic.TypeCorrect);
        Assert.False(diagnostic.GeometryEvaluated);
    }

    private static DimensionCase Case() => new()
    {
        Id = "flags_case",
        Seed = 12345,
        DimensionType = DimensionType.Linear,
        ExpectedValue = 5200,
        P1 = new Point2D(0, 0),
        P2 = new Point2D(5200, 0),
        DimensionLinePoint = new Point2D(2600, 350),
        TextPosition = new Point2D(2600, 350),
        DrawingScale = 100,
        ExpectedResult = ExpectedResult.Recognized,
        ExpectedDimensions = 1,
        ExpectedConfidenceClass = ConfidenceClass.High,
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
        DetectedDimensions = 1,
        Value = expected.ExpectedValue,
        P1 = expected.P1,
        P2 = expected.P2,
        DimensionLinePoint = expected.DimensionLinePoint,
        DimensionType = expected.DimensionType,
        DrawingScale = expected.DrawingScale,
        ConfidenceClass = ConfidenceClass.High
    };

    private static CaseGeometryTrace Trace(DimensionCase expected) => new()
    {
        CaseId = expected.Id,
        DrawingScale = expected.DrawingScale,
        DefinitionPoint1 = Point(expected.P1, expected.DrawingScale),
        DefinitionPoint2 = Point(expected.P2, expected.DrawingScale),
        DimensionLineLocation = Point(expected.DimensionLinePoint, expected.DrawingScale),
        TextAnchor = Point(expected.TextPosition, expected.DrawingScale),
        SceneProvenanceIds = new List<string>
        {
            expected.Id + ":primary:ext:1",
            expected.Id + ":primary:ext:2",
            expected.Id + ":primary:dimline"
        },
        CoreResult = new CoreGeometrySnapshot
        {
            DefinitionPoint1Paper = new Point2D(0, 0),
            DefinitionPoint2Paper = new Point2D(52, 0),
            DimensionLinePointPaper = new Point2D(26, 3.5),
            DefinitionPoint1World = expected.P1,
            DefinitionPoint2World = expected.P2,
            DimensionLinePointWorld = expected.DimensionLinePoint,
            BrokenDimensionLine = false,
            ProvenanceIds = new List<string>
            {
                expected.Id + ":primary:ext:1",
                expected.Id + ":primary:ext:2",
                expected.Id + ":primary:dimline"
            }
        }
    };

    private static GeometryPointTrace Point(Point2D world, double scale) => new()
    {
        ExpectedWorld = world,
        CleanPaper = new Point2D(world.X / scale, world.Y / scale),
        CoreInputPaper = new Point2D(world.X / scale, world.Y / scale),
        InjectedPaperDelta = new Point2D(0, 0)
    };
}
