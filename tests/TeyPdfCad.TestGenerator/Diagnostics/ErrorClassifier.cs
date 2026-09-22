using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

/// <summary>
/// TEST-003 diagnostic classifier. It never changes expected labels or Core output;
/// it only explains mismatches after Semantic Core has run.
/// </summary>
public sealed class ErrorClassifier
{
    private readonly TestConfig _config;

    public ErrorClassifier(TestConfig? config = null)
    {
        _config = config ?? new TestConfig();
    }

    public CaseDiagnostic Classify(
        DimensionCase expected,
        ActualDimensionResult actual,
        CaseGeometryTrace trace)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(trace);

        var legacyWrongPoints = IsLegacyWrongPoints(expected, actual);

        if (expected.ExpectedResult == ExpectedResult.Rejected)
        {
            if (actual.Result == ExpectedResult.Rejected)
                return Terminal(expected, DiagnosticCategory.Correct, false, legacyWrongPoints,
                    detectionCorrect: true, abstentionCorrect: null, "Expected rejection was preserved.");

            if (actual.Result == ExpectedResult.Ambiguous)
                return Terminal(expected, DiagnosticCategory.WrongAbstention, true, legacyWrongPoints,
                    detectionCorrect: false, abstentionCorrect: false, "Expected rejection but Core abstained.");

            return Terminal(expected, DiagnosticCategory.UnexpectedDetection, true, legacyWrongPoints,
                detectionCorrect: false, abstentionCorrect: null, "Core detected a dimension in a negative case.");
        }

        if (expected.ExpectedResult == ExpectedResult.Ambiguous)
        {
            if (actual.Result == ExpectedResult.Ambiguous)
                return Terminal(expected, DiagnosticCategory.ExpectedAbstention, false, legacyWrongPoints,
                    detectionCorrect: true, abstentionCorrect: true, "Borderline evidence correctly abstained.");

            return Terminal(expected, DiagnosticCategory.WrongAbstention, true, legacyWrongPoints,
                detectionCorrect: false, abstentionCorrect: false,
                actual.Result == ExpectedResult.Recognized
                    ? "Borderline evidence was forced into recognition."
                    : "Borderline evidence was hard-rejected instead of represented as ambiguous.");
        }

        if (actual.Result == ExpectedResult.Rejected)
            return Terminal(expected, DiagnosticCategory.MissedDetection, true, legacyWrongPoints,
                detectionCorrect: false, abstentionCorrect: null, "Expected dimension was not detected.");

        if (actual.Result == ExpectedResult.Ambiguous)
            return Terminal(expected, DiagnosticCategory.WrongAbstention, true, legacyWrongPoints,
                detectionCorrect: false, abstentionCorrect: false, "Recognizable dimension was returned as ambiguous.");

        var countCorrect = actual.DetectedDimensions == expected.ExpectedDimensions;
        if (!countCorrect)
        {
            var category = expected.DimensionType == DimensionType.Chain
                ? DiagnosticCategory.ChainMismatch
                : DiagnosticCategory.WrongDimensionCount;
            return SemanticTerminal(expected, actual, category, legacyWrongPoints,
                countCorrect: false,
                reason: $"Expected {expected.ExpectedDimensions} dimensions, Core returned {actual.DetectedDimensions}.");
        }

        var valueCorrect = actual.Value is not null &&
                           Math.Abs(actual.Value.Value - expected.ExpectedValue) <= _config.ValueToleranceAbsolute;
        if (!valueCorrect)
            return SemanticTerminal(expected, actual, DiagnosticCategory.WrongValue, legacyWrongPoints,
                countCorrect: true,
                valueCorrect: false,
                reason: $"Expected value {expected.ExpectedValue:0.######}, Core returned {actual.Value?.ToString("0.######") ?? "<null>"}.");

        var scaleCorrect = actual.DrawingScale is not null &&
                           Math.Abs(actual.DrawingScale.Value - expected.DrawingScale) <= _config.ScaleTolerance;
        if (!scaleCorrect)
            return SemanticTerminal(expected, actual, DiagnosticCategory.WrongScale, legacyWrongPoints,
                countCorrect: true, valueCorrect: true, scaleCorrect: false,
                reason: $"Expected scale 1:{expected.DrawingScale:0.######}, Core returned {actual.DrawingScale?.ToString("0.######") ?? "<null>"}.");

        var typeCorrect = TypesEquivalent(expected, actual);
        if (!typeCorrect)
        {
            var category = expected.DimensionType == DimensionType.Chain
                ? DiagnosticCategory.ChainMismatch
                : DiagnosticCategory.WrongDimensionType;
            return SemanticTerminal(expected, actual, category, legacyWrongPoints,
                countCorrect: true, valueCorrect: true, scaleCorrect: true, typeCorrect: false,
                reason: $"Expected type {expected.DimensionType}, Core returned {actual.DimensionType?.ToString() ?? "<null>"}.");
        }

        var geometry = ClassifyGeometry(expected, actual, trace);
        var primary = geometry.Any(x => x.Status == GeometryCheckStatus.Wrong)
            ? DiagnosticCategory.WrongGeometry
            : geometry.Any(x => x.Status == GeometryCheckStatus.NumericTolerance)
                ? DiagnosticCategory.NumericTolerance
                : geometry.Any(x => x.Status == GeometryCheckStatus.ExpectedNoisePropagation)
                    ? DiagnosticCategory.ExpectedNoisePropagation
                    : DiagnosticCategory.Correct;

        var errors = geometry.Where(x => x.Error is not null).Select(x => x.Error!).ToList();
        var maxPaper = errors.Count == 0 ? 0 : errors.Max(x => x.ActualPaperError);
        var maxWorld = errors.Count == 0 ? 0 : errors.Max(x => x.ActualWorldError);
        var bias = CalculateResidualBias(trace);
        var geometryCorrect = primary is DiagnosticCategory.Correct
            or DiagnosticCategory.ExpectedNoisePropagation
            or DiagnosticCategory.NumericTolerance;

        var reasons = geometry
            .Where(x => x.Status is GeometryCheckStatus.Wrong or GeometryCheckStatus.NumericTolerance or GeometryCheckStatus.ExpectedNoisePropagation)
            .Select(x => $"{x.Component}: {x.Explanation}")
            .ToList();

        if (reasons.Count == 0)
            reasons.Add("Semantic and available geometry checks match.");

        return new CaseDiagnostic
        {
            CaseId = expected.Id,
            Seed = expected.Seed,
            Category = primary,
            IsRealCoreDefect = primary == DiagnosticCategory.WrongGeometry,
            WasLegacyWrongPoints = legacyWrongPoints,
            DetectionCorrect = true,
            ValueCorrect = true,
            ScaleCorrect = true,
            TypeCorrect = true,
            CountCorrect = true,
            GeometryEvaluated = true,
            GeometryCorrect = geometryCorrect,
            AbstentionCorrect = null,
            MaxPaperError = maxPaper,
            MaxWorldError = maxWorld,
            SignedPaperDx = bias.Dx,
            SignedPaperDy = bias.Dy,
            GeometryChecks = geometry,
            Reasons = reasons
        };
    }

    private List<GeometrySubcheck> ClassifyGeometry(
        DimensionCase expected,
        ActualDimensionResult actual,
        CaseGeometryTrace trace)
    {
        var snapshot = trace.CoreResult;
        if (snapshot is null)
        {
            return new List<GeometrySubcheck>
            {
                WrongUnavailable("extension-line points", "Core geometry snapshot is missing."),
                WrongUnavailable("dimension-line location", "Core geometry snapshot is missing."),
                Unavailable("text anchor", "DimensionCandidate does not expose a semantic text anchor."),
                Unavailable("rotation/orientation", "DimensionCandidate does not expose semantic text orientation."),
                Unavailable("broken dimension line", "Core provenance is unavailable."),
                Unavailable("source/provenance IDs", "Core provenance is unavailable.")
            };
        }

        var checks = new List<GeometrySubcheck>();
        var pairing = ChooseEndpointPairing(trace, snapshot);
        checks.Add(CheckPoint("extension-line point 1", pairing.Expected1, pairing.ActualPaper1, pairing.ActualWorld1, expected.DrawingScale));
        checks.Add(CheckPoint("extension-line point 2", pairing.Expected2, pairing.ActualPaper2, pairing.ActualWorld2, expected.DrawingScale));
        checks.Add(CheckDimensionLineLocation(
            trace.DimensionLineLocation,
            snapshot.DimensionLinePointPaper,
            snapshot.DimensionLinePointWorld,
            expected.DrawingScale,
            trace.ExpectedRotationDegrees,
            snapshot.DimensionLineRotationDegrees));

        checks.Add(Unavailable("text anchor", "DimensionCandidate does not expose a reconstructed text anchor."));
        checks.Add(CheckDimensionLineRotation(
            trace.ExpectedRotationDegrees,
            trace.InjectedNoise.AngularSkewDegrees,
            snapshot.DimensionLineRotationDegrees));

        if (snapshot.BrokenDimensionLine is null)
        {
            checks.Add(Unavailable("broken dimension line", "Broken-line state cannot be derived from Core provenance."));
        }
        else
        {
            var correct = snapshot.BrokenDimensionLine.Value == trace.ExpectedBrokenDimensionLine;
            checks.Add(new GeometrySubcheck
            {
                Component = "broken dimension line",
                Status = correct ? GeometryCheckStatus.Correct : GeometryCheckStatus.Wrong,
                Explanation = correct
                    ? "Broken-line evidence matches the input geometry."
                    : $"Expected broken={trace.ExpectedBrokenDimensionLine}, Core provenance implies broken={snapshot.BrokenDimensionLine.Value}."
            });
        }

        if (snapshot.ProvenanceIds.Count == 0 || trace.SceneProvenanceIds.Count == 0)
        {
            checks.Add(Unavailable("source/provenance IDs", "Comparable provenance IDs are unavailable."));
        }
        else
        {
            var sceneIds = trace.SceneProvenanceIds.ToHashSet(StringComparer.Ordinal);
            var unknown = snapshot.ProvenanceIds.Where(id => !sceneIds.Contains(id)).ToList();
            checks.Add(new GeometrySubcheck
            {
                Component = "source/provenance IDs",
                Status = unknown.Count == 0 ? GeometryCheckStatus.Correct : GeometryCheckStatus.Wrong,
                Explanation = unknown.Count == 0
                    ? $"All {snapshot.ProvenanceIds.Count} Core provenance IDs resolve to supplied primitives."
                    : "Core returned provenance IDs absent from the supplied scene: " + string.Join(", ", unknown.Take(5))
            });
        }

        return checks;
    }

    private static GeometrySubcheck CheckDimensionLineLocation(
        GeometryPointTrace expected,
        Point2D? actualPaper,
        Point2D? actualWorld,
        double drawingScale,
        double expectedRotationDegrees,
        double? actualRotationDegrees)
    {
        if (expected.CoreInputPaper is null
            || actualPaper is null
            || actualWorld is null
            || actualRotationDegrees is null)
        {
            return WrongUnavailable(
                "dimension-line location",
                "Required Core/input dimension-line point or rotation is missing.");
        }

        var expectedRadians = expectedRotationDegrees * Math.PI / 180.0;
        var actualRadians = actualRotationDegrees.Value * Math.PI / 180.0;

        var expectedNormalX = -Math.Sin(expectedRadians);
        var expectedNormalY = Math.Cos(expectedRadians);
        var actualNormalX = -Math.Sin(actualRadians);
        var actualNormalY = Math.Cos(actualRadians);

        // Orient the actual normal consistently with the expected normal so
        // signed offsets remain comparable modulo 180 degrees.
        if (expectedNormalX * actualNormalX + expectedNormalY * actualNormalY < 0)
        {
            actualNormalX = -actualNormalX;
            actualNormalY = -actualNormalY;
        }

        static double SignedOffset(
            Point2D point,
            Point2D origin,
            double normalX,
            double normalY)
            => (point.X - origin.X) * normalX
               + (point.Y - origin.Y) * normalY;

        var injectedOffset = SignedOffset(
            expected.CoreInputPaper.Value,
            expected.CleanPaper,
            actualNormalX,
            actualNormalY);
        var actualOffset = SignedOffset(
            actualPaper.Value,
            expected.CleanPaper,
            actualNormalX,
            actualNormalY);

        // Compare infinite-line offset, not an arbitrary representative point.
        // A different point on the same line is semantically identical.
        var error = GeometryTolerance.Calculate(new PointDiagnosticInput
        {
            ExpectedWorld = new Point2D(0, 0),
            ActualWorld = new Point2D(actualOffset * drawingScale, 0),
            CleanPaper = new Point2D(0, 0),
            CoreInputPaper = new Point2D(injectedOffset, 0),
            ActualCorePaper = new Point2D(actualOffset, 0),
            DrawingScale = drawingScale
        });

        var status = error.Category switch
        {
            DiagnosticCategory.Correct => GeometryCheckStatus.Correct,
            DiagnosticCategory.ExpectedNoisePropagation => GeometryCheckStatus.ExpectedNoisePropagation,
            DiagnosticCategory.NumericTolerance => GeometryCheckStatus.NumericTolerance,
            _ => GeometryCheckStatus.Wrong
        };

        return new GeometrySubcheck
        {
            Component = "dimension-line location",
            Status = status,
            Error = error,
            Explanation = status switch
            {
                GeometryCheckStatus.Correct =>
                    "Infinite dimension-line offset matches; along-line translation is semantically equivalent.",
                GeometryCheckStatus.ExpectedNoisePropagation =>
                    $"Perpendicular line-offset error {error.ActualPaperError:0.########} is inside injected envelope {error.InjectedPaperError:0.########}.",
                GeometryCheckStatus.NumericTolerance =>
                    $"Perpendicular line-offset error {error.ActualPaperError:0.########} exceeds injected envelope only within fixed numeric epsilon.",
                _ =>
                    $"Perpendicular line-offset error {error.ActualPaperError:0.########} exceeds allowed {error.AllowedPaperError:0.########}."
            }
        };
    }

    private static GeometrySubcheck CheckDimensionLineRotation(
        double expectedRotationDegrees,
        double injectedSkewDegrees,
        double? actualRotationDegrees)
    {
        if (actualRotationDegrees is null)
            return WrongUnavailable("rotation/orientation", "Core dimension-line rotation is missing.");

        var actualError = ParallelAngleDifferenceDegrees(
            expectedRotationDegrees,
            actualRotationDegrees.Value);
        var injectedError = Math.Abs(injectedSkewDegrees);
        const double numericAngleEpsilon = 1e-6;

        var status = actualError <= injectedError + 1e-12
            ? injectedError > 1e-12
                ? GeometryCheckStatus.ExpectedNoisePropagation
                : GeometryCheckStatus.Correct
            : actualError <= injectedError + numericAngleEpsilon + 1e-12
                ? GeometryCheckStatus.NumericTolerance
                : GeometryCheckStatus.Wrong;

        return new GeometrySubcheck
        {
            Component = "rotation/orientation",
            Status = status,
            Explanation = status switch
            {
                GeometryCheckStatus.Correct =>
                    "Dimension-line rotation matches.",
                GeometryCheckStatus.ExpectedNoisePropagation =>
                    $"Angular error {actualError:0.########}° is inside injected skew {injectedError:0.########}°.",
                GeometryCheckStatus.NumericTolerance =>
                    $"Angular error {actualError:0.########}° exceeds injected skew only within numeric epsilon.",
                _ =>
                    $"Angular error {actualError:0.########}° exceeds allowed injected skew {injectedError:0.########}°."
            }
        };
    }

    private static GeometrySubcheck CheckPoint(
        string component,
        GeometryPointTrace expected,
        Point2D? actualPaper,
        Point2D? actualWorld,
        double drawingScale)
    {
        if (expected.CoreInputPaper is null || actualPaper is null || actualWorld is null)
            return WrongUnavailable(component, "Required Core/input point is missing.");

        var error = GeometryTolerance.Calculate(new PointDiagnosticInput
        {
            ExpectedWorld = expected.ExpectedWorld,
            ActualWorld = actualWorld.Value,
            CleanPaper = expected.CleanPaper,
            CoreInputPaper = expected.CoreInputPaper.Value,
            ActualCorePaper = actualPaper.Value,
            DrawingScale = drawingScale
        });

        var status = error.Category switch
        {
            DiagnosticCategory.Correct => GeometryCheckStatus.Correct,
            DiagnosticCategory.ExpectedNoisePropagation => GeometryCheckStatus.ExpectedNoisePropagation,
            DiagnosticCategory.NumericTolerance => GeometryCheckStatus.NumericTolerance,
            _ => GeometryCheckStatus.Wrong
        };

        return new GeometrySubcheck
        {
            Component = component,
            Status = status,
            Error = error,
            Explanation = status switch
            {
                GeometryCheckStatus.Correct => "No material point error.",
                GeometryCheckStatus.ExpectedNoisePropagation =>
                    $"Paper error {error.ActualPaperError:0.########} is inside injected envelope {error.InjectedPaperError:0.########}.",
                GeometryCheckStatus.NumericTolerance =>
                    $"Paper error {error.ActualPaperError:0.########} exceeds injected envelope only within fixed numeric epsilon.",
                _ => $"Paper error {error.ActualPaperError:0.########} exceeds allowed {error.AllowedPaperError:0.########}."
            }
        };
    }

    private static EndpointPairing ChooseEndpointPairing(CaseGeometryTrace trace, CoreGeometrySnapshot snapshot)
    {
        if (snapshot.DefinitionPoint1Paper is null || snapshot.DefinitionPoint2Paper is null ||
            snapshot.DefinitionPoint1World is null || snapshot.DefinitionPoint2World is null)
        {
            return new EndpointPairing(
                trace.DefinitionPoint1, null, null,
                trace.DefinitionPoint2, null, null);
        }

        var a1 = snapshot.DefinitionPoint1Paper.Value;
        var a2 = snapshot.DefinitionPoint2Paper.Value;
        var direct = trace.DefinitionPoint1.CleanPaper.DistanceTo(a1) + trace.DefinitionPoint2.CleanPaper.DistanceTo(a2);
        var swapped = trace.DefinitionPoint1.CleanPaper.DistanceTo(a2) + trace.DefinitionPoint2.CleanPaper.DistanceTo(a1);

        return direct <= swapped
            ? new EndpointPairing(trace.DefinitionPoint1, a1, snapshot.DefinitionPoint1World,
                trace.DefinitionPoint2, a2, snapshot.DefinitionPoint2World)
            : new EndpointPairing(trace.DefinitionPoint1, a2, snapshot.DefinitionPoint2World,
                trace.DefinitionPoint2, a1, snapshot.DefinitionPoint1World);
    }

    private static (double Dx, double Dy) CalculateResidualBias(CaseGeometryTrace trace)
    {
        var snapshot = trace.CoreResult;
        if (snapshot is null)
            return (0, 0);

        var pairing = ChooseEndpointPairing(trace, snapshot);
        var samples = new List<(Point2D Input, Point2D Actual)>();
        Add(pairing.Expected1.CoreInputPaper, pairing.ActualPaper1);
        Add(pairing.Expected2.CoreInputPaper, pairing.ActualPaper2);
        Add(trace.DimensionLineLocation.CoreInputPaper, snapshot.DimensionLinePointPaper);

        if (samples.Count == 0)
            return (0, 0);

        return (
            samples.Average(x => x.Actual.X - x.Input.X),
            samples.Average(x => x.Actual.Y - x.Input.Y));

        void Add(Point2D? input, Point2D? actual)
        {
            if (input is not null && actual is not null)
                samples.Add((input.Value, actual.Value));
        }
    }

    private bool IsLegacyWrongPoints(DimensionCase expected, ActualDimensionResult actual)
    {
        if (expected.ExpectedResult != ExpectedResult.Recognized || actual.Result != ExpectedResult.Recognized)
            return false;
        if (actual.DetectedDimensions != expected.ExpectedDimensions)
            return false;
        if (!TypesEquivalent(expected, actual))
            return false;
        if (actual.Value is null || Math.Abs(actual.Value.Value - expected.ExpectedValue) > _config.ValueToleranceAbsolute)
            return false;
        if (actual.P1 is null || actual.P2 is null || actual.DimensionLinePoint is null)
            return true;

        var direct = Math.Max(expected.P1.DistanceTo(actual.P1.Value), expected.P2.DistanceTo(actual.P2.Value));
        var swapped = Math.Max(expected.P1.DistanceTo(actual.P2.Value), expected.P2.DistanceTo(actual.P1.Value));
        var endpoints = Math.Min(direct, swapped);
        var dimline = expected.DimensionLinePoint.DistanceTo(actual.DimensionLinePoint.Value);
        return endpoints > 0.05 || dimline > 0.05;
    }

    private static double ParallelAngleDifferenceDegrees(double first, double second)
    {
        var difference = Math.Abs(first - second) % 180.0;
        return Math.Min(difference, 180.0 - difference);
    }

    private static bool TypesEquivalent(DimensionCase expected, ActualDimensionResult actual)
    {
        if (actual.DimensionType == expected.DimensionType)
            return true;
        if (!actual.IsDimensionTypeAmbiguous || actual.DimensionType is null)
            return false;

        return IsLinearFamily(expected.DimensionType)
               && IsLinearFamily(actual.DimensionType.Value);
    }

    private static bool IsLinearFamily(DimensionType type)
        => type is DimensionType.Linear or DimensionType.Rotated or DimensionType.Aligned;

    private static CaseDiagnostic Terminal(
        DimensionCase expected,
        DiagnosticCategory category,
        bool defect,
        bool legacyWrongPoints,
        bool detectionCorrect,
        bool? abstentionCorrect,
        string reason)
    {
        return new CaseDiagnostic
        {
            CaseId = expected.Id,
            Seed = expected.Seed,
            Category = category,
            IsRealCoreDefect = defect,
            WasLegacyWrongPoints = legacyWrongPoints,
            DetectionCorrect = detectionCorrect,
            GeometryEvaluated = false,
            GeometryCorrect = false,
            AbstentionCorrect = abstentionCorrect,
            Reasons = new List<string> { reason }
        };
    }

    private static CaseDiagnostic SemanticTerminal(
        DimensionCase expected,
        ActualDimensionResult actual,
        DiagnosticCategory category,
        bool legacyWrongPoints,
        bool? valueCorrect = null,
        bool? scaleCorrect = null,
        bool? typeCorrect = null,
        bool? countCorrect = null,
        string reason = "Semantic mismatch.")
    {
        return new CaseDiagnostic
        {
            CaseId = expected.Id,
            Seed = expected.Seed,
            Category = category,
            IsRealCoreDefect = true,
            WasLegacyWrongPoints = legacyWrongPoints,
            DetectionCorrect = true,
            ValueCorrect = valueCorrect,
            ScaleCorrect = scaleCorrect,
            TypeCorrect = typeCorrect,
            CountCorrect = countCorrect,
            GeometryEvaluated = false,
            GeometryCorrect = false,
            Reasons = new List<string> { reason }
        };
    }

    private static GeometrySubcheck WrongUnavailable(string component, string reason)
        => new()
        {
            Component = component,
            Status = GeometryCheckStatus.Wrong,
            Explanation = reason
        };

    private static GeometrySubcheck Unavailable(string component, string reason)
        => new()
        {
            Component = component,
            Status = GeometryCheckStatus.Unavailable,
            Explanation = reason
        };

    private sealed record EndpointPairing(
        GeometryPointTrace Expected1,
        Point2D? ActualPaper1,
        Point2D? ActualWorld1,
        GeometryPointTrace Expected2,
        Point2D? ActualPaper2,
        Point2D? ActualWorld2);
}
