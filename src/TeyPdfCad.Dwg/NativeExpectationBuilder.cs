using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Dwg;

internal static class NativeExpectationBuilder
{
    public static NativeWriteManifest Build(
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage,
        IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage,
        IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        var expected = new Dictionary<string, ExpectedCandidate>(StringComparer.Ordinal);
        var sheetsByPage = plan.Sheets.ToDictionary(sheet => sheet.PageNumber);

        foreach (var page in source.Pages)
        {
            var sheet = sheetsByPage[page.Number];
            var hatchRecognition = hatchRecognitionByPage is not null
                && hatchRecognitionByPage.TryGetValue(page.Number, out var suppliedHatch)
                    ? suppliedHatch
                    : new HatchRecognizer().Recognize(page.Entities, page.Number);
            var semantics = semanticRecognitionByPage is not null
                && semanticRecognitionByPage.TryGetValue(page.Number, out var suppliedSemantics)
                    ? suppliedSemantics
                    : null;
            var replacementPlan = sourceReplacementPlansByPage is not null
                && sourceReplacementPlansByPage.TryGetValue(page.Number, out var suppliedPlan)
                    ? suppliedPlan
                    : new SourceReplacementPlanner().BuildPlan(
                        page.Entities,
                        semantics,
                        hatchRecognition,
                        page.Number);
            var deferred = replacementPlan.DeferredCandidateKeys
                .ToHashSet(StringComparer.Ordinal);
            var authorizedCandidates = NativeCandidateAuthorization.GetAuthorizedCandidateIds(
                replacementPlan,
                page.Number,
                authorizedSuppressedSources);
            var equivalence = SourceEquivalenceAssessor.Build(
                page,
                semantics,
                hatchRecognition);

            var expectationDocument = new CadDocument();
            var styles = new AcadSharpStyleCatalog(expectationDocument);

            foreach (var hatchCandidate in hatchRecognition.NativeHatches.Where(candidate => !candidate.IsSolid))
            {
                var candidateId = SourceReplacementPlanner.GetCandidateKey(
                    hatchCandidate,
                    page.Number);
                if (deferred.Contains(candidateId)
                    || !NativeCandidateAuthorization.ShouldEmit(authorizedCandidates, candidateId))
                    continue;

                var boundary = new LwPolyline(
                    hatchCandidate.Boundary.Select(point => new XY(
                        sheet.ModelOriginX + point.X,
                        sheet.ModelOriginY + point.Y)))
                {
                    IsClosed = true
                };
                styles.Apply(boundary, hatchCandidate.Style);

                var angle = hatchCandidate.PatternAngleRadians ?? 0d;
                var spacing = hatchCandidate.PatternSpacingMillimetres ?? 1d;
                var pattern = new HatchPattern("TEYPDFCAD_LINEAR_EXPECTED");
                pattern.Lines.Add(new HatchPattern.Line
                {
                    Angle = angle,
                    BasePoint = new XY(0d, 0d),
                    Offset = new XY(
                        -Math.Sin(angle) * spacing,
                        Math.Cos(angle) * spacing)
                });
                var hatch = new Hatch
                {
                    IsSolid = false,
                    Pattern = pattern,
                    PatternType = HatchPatternType.Custom,
                    PatternScale = 1d
                };
                hatch.Paths.Add(new Hatch.BoundaryPath([boundary]));
                styles.Apply(hatch, hatchCandidate.Style);

                AddExpected(
                    expected,
                    equivalence.GetRequired(candidateId),
                    new ExpectedNativeEntity(
                        candidateId,
                        "primary",
                        "Hatch",
                        DwgEntityFingerprint.ComputeGeometry(hatch),
                        Properties(
                            ("minimumArea", "0.000001"),
                            ("boundaryFingerprint", DwgEntityFingerprint.ComputeHatchBoundary(hatch)))));
            }

            if (semantics is null)
                continue;

            foreach (var candidate in semantics.Dimensions)
            {
                var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                if (deferred.Contains(candidateId)
                    || !NativeCandidateAuthorization.ShouldEmit(authorizedCandidates, candidateId))
                    continue;

                var dimension = BuildExpectedDimension(
                    sheet,
                    candidate);
                var dimensionProperties = new List<(string Key, string Value)>
                {
                    ("expectedMeasurement", Number(candidate.ReconstructedMeasurement)),
                    ("measurementTolerance", "0.000001"),
                    ("expectedDimensionText", NativeDimensionTextBuilder.Build(
                        candidate.SourceText,
                        candidate.DisplayedValue)),
                    ("expectedLayer", "PDF_РАЗМЕРЫ"),
                    ("expectedNormal", Point(new XYZ(0d, 0d, 1d))),
                    ("dimensionStyleFingerprint", DwgEntityFingerprint.ComputeDimensionStyle(dimension))
                };
                if (candidate.SourceAppearance?.Text.VisualCenter is { } visualCenter)
                {
                    dimensionProperties.Add((
                        "expectedTextMiddlePoint",
                        Point(new XYZ(
                            sheet.ModelOriginX + visualCenter.X,
                            sheet.ModelOriginY + visualCenter.Y,
                            0d))));
                    dimensionProperties.Add(("expectedTextUserDefinedLocation", "true"));
                }

                AddExpected(
                    expected,
                    equivalence.GetRequired(candidateId),
                    new ExpectedNativeEntity(
                        candidateId,
                        "primary",
                        "Dimension",
                        DwgEntityFingerprint.ComputeGeometry(dimension),
                        Properties(dimensionProperties.ToArray())));
            }

            foreach (var candidate in semantics.Leaders)
            {
                var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                if (deferred.Contains(candidateId)
                    || !NativeCandidateAuthorization.ShouldEmit(authorizedCandidates, candidateId))
                    continue;

                var arrow = new XYZ(
                    sheet.ModelOriginX + candidate.ArrowPoint.X,
                    sheet.ModelOriginY + candidate.ArrowPoint.Y,
                    0d);
                var textPoint = new XYZ(
                    sheet.ModelOriginX + candidate.TextPoint.X,
                    sheet.ModelOriginY + candidate.TextPoint.Y,
                    0d);
                var leader = new Leader
                {
                    ArrowHeadEnabled = true,
                    CreationType = LeaderCreationType.CreatedWithTextAnnotation,
                    PathType = LeaderPathType.StraightLineSegments,
                    TextHeight = 2.5d
                };
                leader.Vertices.Add(arrow);
                leader.Vertices.Add(textPoint);
                var annotation = new TextEntity
                {
                    Value = candidate.Text,
                    InsertPoint = textPoint,
                    Height = 2.5d
                };

                AddExpected(
                    expected,
                    equivalence.GetRequired(candidateId),
                    new ExpectedNativeEntity(
                        candidateId,
                        "primary",
                        "Leader",
                        DwgEntityFingerprint.ComputeGeometry(leader),
                        Properties(("minimumVertices", "2"))),
                    new ExpectedNativeEntity(
                        candidateId,
                        "annotation",
                        "Text",
                        DwgEntityFingerprint.ComputeGeometry(annotation),
                        Properties(
                            ("minimumHeight", "2.5"),
                            ("nonEmpty", "true"),
                            ("expectedText", candidate.Text))));
            }

            foreach (var candidate in semantics.Axes)
            {
                var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                if (deferred.Contains(candidateId)
                    || !NativeCandidateAuthorization.ShouldEmit(authorizedCandidates, candidateId))
                    continue;

                var block = BuildExpectedAxisBlock(expectationDocument, styles);
                var dx = candidate.End.X - candidate.Start.X;
                var dy = candidate.End.Y - candidate.Start.Y;
                var insert = new Insert(block)
                {
                    InsertPoint = new XYZ(
                        sheet.ModelOriginX + candidate.Start.X,
                        sheet.ModelOriginY + candidate.Start.Y,
                        0d),
                    XScale = Math.Sqrt(dx * dx + dy * dy),
                    YScale = 1d,
                    ZScale = 1d,
                    Rotation = Math.Atan2(dy, dx),
                    Layer = styles.GetAnnotationLayer("PDF_ОСИ")
                };

                AddExpected(
                    expected,
                    equivalence.GetRequired(candidateId),
                    new ExpectedNativeEntity(
                        candidateId,
                        "primary",
                        "Insert",
                        DwgEntityFingerprint.ComputeGeometry(insert),
                        Properties(
                            ("blockName", "TEY_AXIS"),
                            ("blockDefinitionFingerprint", DwgEntityFingerprint.ComputeBlockDefinition(insert)),
                            ("minimumScale", "0.000001"),
                            ("minimumLength", Number(Math.Sqrt(dx * dx + dy * dy))))));
            }

            foreach (var candidate in semantics.Levels)
            {
                var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                if (deferred.Contains(candidateId)
                    || !NativeCandidateAuthorization.ShouldEmit(authorizedCandidates, candidateId))
                    continue;

                var block = BuildExpectedLevelBlock(expectationDocument, styles);
                var dx = candidate.TextPoint.X - candidate.MarkerPoint.X;
                var dy = candidate.TextPoint.Y - candidate.MarkerPoint.Y;
                var insert = new Insert(block)
                {
                    InsertPoint = new XYZ(
                        sheet.ModelOriginX + candidate.MarkerPoint.X,
                        sheet.ModelOriginY + candidate.MarkerPoint.Y,
                        0d),
                    Rotation = Math.Atan2(dy, dx),
                    Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ")
                };
                var attribute = insert.Attributes.Single();
                attribute.Value = candidate.Value;
                attribute.InsertPoint = new XYZ(
                    sheet.ModelOriginX + candidate.TextPoint.X,
                    sheet.ModelOriginY + candidate.TextPoint.Y,
                    0d);

                AddExpected(
                    expected,
                    equivalence.GetRequired(candidateId),
                    new ExpectedNativeEntity(
                        candidateId,
                        "primary",
                        "Insert",
                        DwgEntityFingerprint.ComputeGeometry(insert),
                        Properties(
                            ("blockName", "TEY_LEVEL"),
                            ("blockDefinitionFingerprint", DwgEntityFingerprint.ComputeBlockDefinition(insert)),
                            ("minimumScale", "0.000001"))),
                    new ExpectedNativeEntity(
                        candidateId,
                        "attribute",
                        "AttributeEntity",
                        DwgEntityFingerprint.ComputeGeometry(attribute),
                        Properties(
                            ("attributeTag", "LEVEL"),
                            ("nonEmptyValue", "true"),
                            ("expectedAttributeValue", candidate.Value))));
            }

            foreach (var candidate in semantics.ArcDimensions)
            {
                var candidateId = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                if (deferred.Contains(candidateId)
                    || !NativeCandidateAuthorization.ShouldEmit(authorizedCandidates, candidateId))
                    continue;

                var dimension = BuildExpectedArcDimension(
                    styles,
                    sheet,
                    candidate);

                AddExpected(
                    expected,
                    equivalence.GetRequired(candidateId),
                    new ExpectedNativeEntity(
                        candidateId,
                        "primary",
                        "Dimension",
                        DwgEntityFingerprint.ComputeGeometry(dimension),
                        Properties(
                            ("expectedMeasurement", Number(dimension.Measurement)),
                            ("measurementTolerance", "0.000001"),
                            ("expectedDimensionText", candidate.SourceText),
                            ("dimensionStyleFingerprint", DwgEntityFingerprint.ComputeDimensionStyle(dimension)))));
            }
        }

        return new NativeWriteManifest(expected);
    }

    private static Dimension BuildExpectedDimension(
        SheetPlan sheet,
        DimensionCandidate candidate)
    {
        var first = new XYZ(
            sheet.ModelOriginX + candidate.DefinitionPoint1.X,
            sheet.ModelOriginY + candidate.DefinitionPoint1.Y,
            0d);
        var second = new XYZ(
            sheet.ModelOriginX + candidate.DefinitionPoint2.X,
            sheet.ModelOriginY + candidate.DefinitionPoint2.Y,
            0d);
        var definition = new XYZ(
            sheet.ModelOriginX + candidate.DimensionLinePoint.X,
            sheet.ModelOriginY + candidate.DimensionLinePoint.Y,
            0d);

        Dimension dimension = candidate.Kind == DimensionKind.Rotated
            ? new DimensionLinear
            {
                FirstPoint = first,
                SecondPoint = second,
                Rotation = candidate.RotationRadians
                    ?? Math.Atan2(second.Y - first.Y, second.X - first.X)
            }
            : new DimensionAligned(first, second);

        dimension.DefinitionPoint = definition;
        dimension.Normal = new XYZ(0d, 0d, 1d);

        // Intentionally do not call AcadSharpStyleCatalog.GetDimensionStyle here.
        // This is the independent pre-write proof path: a defect in the writer's
        // style helper must not automatically reproduce itself in the manifest.
        dimension.Style = BuildIndependentExpectedDimensionStyle(candidate);
        dimension.Text = NativeDimensionTextBuilder.Build(
            candidate.SourceText,
            candidate.DisplayedValue);
        if (candidate.SourceAppearance?.Text.VisualCenter is { } visualCenter)
        {
            dimension.TextMiddlePoint = new XYZ(
                sheet.ModelOriginX + visualCenter.X,
                sheet.ModelOriginY + visualCenter.Y,
                0d);
            dimension.IsTextUserDefinedLocation = true;
        }
        return dimension;
    }

    private static DimensionStyle BuildIndependentExpectedDimensionStyle(DimensionCandidate candidate)
    {
        var appearance = candidate.SourceAppearance;
        var canonicalScale = Math.Round(candidate.DrawingScale, 6);
        var tickSize = IndependentlyResolveObliqueTickSize(appearance);
        var scaleToken = canonicalScale.ToString("0.######", CultureInfo.InvariantCulture).Replace('.', '_');
        var tickToken = tickSize.HasValue
            ? "_TICK_" + Math.Round(tickSize.Value, 6)
                .ToString("0.######", CultureInfo.InvariantCulture)
                .Replace('.', '_')
            : string.Empty;
        var appearanceToken = appearance is null
            ? string.Empty
            : "_SRC_" + IndependentDimensionAppearanceToken(appearance);
        var name = $"TEYPDFCAD_SCALE_{scaleToken}{tickToken}{appearanceToken}";

        var style = new DimensionStyle(name)
        {
            LinearScaleFactor = canonicalScale,
            TextHeight = IndependentTextHeight(appearance),
            ArrowSize = 2.5d,
            TickSize = tickSize ?? 0d,
            DimensionLineExtension = 0d,
            ExtensionLineOffset = appearance is null ? 0.75d : 0d,
            ExtensionLineExtension = appearance is null
                ? 1.25d
                : IndependentExtensionBeyondDimensionLine(appearance) ?? 0d,
            ScaleFactor = 1d,
            Style = new TextStyle("TEYPDFCAD_TEXT")
            {
                Filename = "arial.ttf",
                Height = 0d,
                Width = 1d
            }
        };

        if (appearance is not null)
        {
            style.DimensionLineColor = IndependentPdfColor(appearance.DimensionLine.RgbColor);
            style.TextColor = IndependentPdfColor(appearance.Text.RgbColor);
            if (appearance.ExtensionLines.Count == 2)
                style.ExtensionLineColor = IndependentPdfColor(appearance.ExtensionLines[0].RgbColor);

            if (appearance.DimensionLine.StrokeWidthMm is { } dimensionWidth)
                style.DimensionLineWeight = IndependentLineWeight(dimensionWidth);
            if (appearance.ExtensionLines.Count == 2
                && appearance.ExtensionLines[0].StrokeWidthMm is { } extensionWidth)
            {
                style.ExtensionLineWeight = IndependentLineWeight(extensionWidth);
            }

            style.LineType = IndependentLineType(appearance.DimensionLine.DashPatternMm);
            if (appearance.ExtensionLines.Count == 2)
            {
                style.LineTypeExt1 = IndependentLineType(appearance.ExtensionLines[0].DashPatternMm);
                style.LineTypeExt2 = IndependentLineType(appearance.ExtensionLines[1].DashPatternMm);
            }
        }

        return style;
    }

    private static double IndependentTextHeight(DimensionSourceAppearance? appearance)
    {
        var height = appearance?.Text.HeightMm;
        return height.HasValue && double.IsFinite(height.Value) && height.Value > 1e-9
            ? height.Value
            : 2.5d;
    }

    private static Color IndependentPdfColor(int? rgb)
        => Color.FromTrueColor((uint)(rgb ?? 0));

    private static LineWeightType IndependentLineWeight(double millimetres)
    {
        var targetHundredths = millimetres * 100d;
        return Enum.GetValues<LineWeightType>()
            .Where(value => value is not LineWeightType.ByBlock
                and not LineWeightType.ByLayer
                and not LineWeightType.ByDIPs
                and not LineWeightType.Default)
            .MinBy(value => Math.Abs((int)value - targetHundredths));
    }

    private static LineType IndependentLineType(IReadOnlyList<double> patternMm)
    {
        var normalized = IndependentNormalizeDash(patternMm);
        if (normalized.Count == 0)
            return new LineType("Continuous");

        var canonicalPattern = normalized.Select(value =>
            Math.Abs(value).ToString("0.######", CultureInfo.InvariantCulture));
        var lineType = new LineType($"PDF_DASH_MM_{string.Join("_", canonicalPattern)}");
        for (var index = 0; index < normalized.Count; index++)
        {
            lineType.AddSegment(new LineType.Segment
            {
                Length = index % 2 == 0
                    ? Math.Abs(normalized[index])
                    : -Math.Abs(normalized[index])
            });
        }
        return lineType;
    }

    private static IReadOnlyList<double> IndependentNormalizeDash(IReadOnlyList<double> pattern)
    {
        if (pattern.Count == 0)
            return [];

        var values = pattern.Select(Math.Abs).ToArray();
        return values.Length % 2 == 0
            ? values
            : [..values, ..values];
    }

    private static double? IndependentExtensionBeyondDimensionLine(DimensionSourceAppearance? appearance)
    {
        if (appearance is null || appearance.ExtensionLines.Count != 2)
            return null;

        var first = IndependentExtensionBeyondDimensionLine(
            appearance.ExtensionLines[0],
            appearance.DimensionLine);
        var second = IndependentExtensionBeyondDimensionLine(
            appearance.ExtensionLines[1],
            appearance.DimensionLine);
        if (!first.HasValue || !second.HasValue)
            return null;
        return Math.Abs(first.Value - second.Value) <= 1e-6
            ? (first.Value + second.Value) / 2d
            : null;
    }

    private static double? IndependentExtensionBeyondDimensionLine(
        DimensionSourceLineAppearance extension,
        DimensionSourceLineAppearance dimensionLine)
    {
        var dim = GeometryMath.Subtract(dimensionLine.End, dimensionLine.Start);
        var ext = GeometryMath.Subtract(extension.End, extension.Start);
        var dimLength = GeometryMath.Length(dim);
        if (dimLength <= 1e-9 || GeometryMath.Length(ext) <= 1e-9)
            return null;

        static double Signed(Point2 point, DimensionSourceLineAppearance line, Point2 vector, double length)
        {
            var relative = GeometryMath.Subtract(point, line.Start);
            return (vector.X * relative.Y - vector.Y * relative.X) / length;
        }

        var firstSigned = Signed(extension.Start, dimensionLine, dim, dimLength);
        var secondSigned = Signed(extension.End, dimensionLine, dim, dimLength);
        if (Math.Abs(firstSigned) <= 1e-9 || Math.Abs(secondSigned) <= 1e-9)
            return 0d;
        if (Math.Sign(firstSigned) == Math.Sign(secondSigned))
            return null;
        return Math.Min(Math.Abs(firstSigned), Math.Abs(secondSigned));
    }

    private static string IndependentDimensionAppearanceToken(DimensionSourceAppearance appearance)
    {
        var canonical = string.Join("|",
            appearance.DimensionLine.RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black",
            appearance.DimensionLine.StrokeWidthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "null",
            IndependentDashToken(appearance.DimensionLine.DashPatternMm),
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[0].RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black"
                : "ext1-color-unknown",
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[0].StrokeWidthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "null"
                : "ext1-width-unknown",
            appearance.ExtensionLines.Count == 2
                ? IndependentDashToken(appearance.ExtensionLines[0].DashPatternMm)
                : "ext1-dash-unknown",
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[1].RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black"
                : "ext2-color-unknown",
            appearance.ExtensionLines.Count == 2
                ? appearance.ExtensionLines[1].StrokeWidthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "null"
                : "ext2-width-unknown",
            appearance.ExtensionLines.Count == 2
                ? IndependentDashToken(appearance.ExtensionLines[1].DashPatternMm)
                : "ext2-dash-unknown",
            appearance.Text.RgbColor?.ToString(CultureInfo.InvariantCulture) ?? "default-black",
            appearance.Text.HeightMm.ToString("R", CultureInfo.InvariantCulture),
            (IndependentExtensionBeyondDimensionLine(appearance) ?? 0d)
                .ToString("R", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static string IndependentDashToken(IReadOnlyList<double> pattern)
        => string.Join(",", IndependentNormalizeDash(pattern)
            .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

    private static double? IndependentlyResolveObliqueTickSize(DimensionSourceAppearance? appearance)
    {
        if (appearance is null || appearance.ArrowLines.Count != 2)
            return null;

        var dimension = GeometryMath.Subtract(
            appearance.DimensionLine.End,
            appearance.DimensionLine.Start);
        var dimensionLength = GeometryMath.Length(dimension);
        if (dimensionLength <= 1e-9)
            return null;
        var unitDimension = GeometryMath.Normalize(dimension);

        var first = IndependentTickAt(
            appearance.DimensionLine.Start,
            appearance.ArrowLines,
            unitDimension,
            appearance.Text.HeightMm);
        var second = IndependentTickAt(
            appearance.DimensionLine.End,
            appearance.ArrowLines,
            unitDimension,
            appearance.Text.HeightMm);
        if (first is null || second is null || ReferenceEquals(first, second))
            return null;

        var firstLength = GeometryMath.Distance(first.Start, first.End);
        var secondLength = GeometryMath.Distance(second.Start, second.End);
        var allowedDifference = Math.Max(1e-6, Math.Max(firstLength, secondLength) * 1e-3);
        if (Math.Abs(firstLength - secondLength) > allowedDifference)
            return null;

        return (firstLength + secondLength) / 2d;
    }

    private static DimensionSourceLineAppearance? IndependentTickAt(
        Point2 endpoint,
        IReadOnlyList<DimensionSourceLineAppearance> arrows,
        Point2 unitDimension,
        double textHeight)
    {
        var matches = new List<DimensionSourceLineAppearance>();
        var contactTolerance = Math.Max(Math.Min(textHeight * 0.05d, 0.1d), 1e-6);

        foreach (var arrow in arrows)
        {
            var arrowVector = GeometryMath.Subtract(arrow.End, arrow.Start);
            var arrowLength = GeometryMath.Length(arrowVector);
            if (arrowLength <= 1e-9)
                continue;
            if (GeometryMath.DistancePointToSegment(endpoint, arrow.Start, arrow.End) > contactTolerance)
                continue;

            var startToEnd = GeometryMath.Subtract(arrow.End, arrow.Start);
            var denominator = GeometryMath.Dot(startToEnd, startToEnd);
            if (denominator <= 1e-12)
                continue;
            var projection = GeometryMath.Dot(
                GeometryMath.Subtract(endpoint, arrow.Start),
                startToEnd) / denominator;
            if (projection is <= 0.05d or >= 0.95d)
                continue;

            var cosine = Math.Abs(GeometryMath.Dot(
                GeometryMath.Normalize(arrowVector),
                unitDimension));
            if (Math.Abs(cosine - Math.Sqrt(0.5d)) > 0.01d)
                continue;

            matches.Add(arrow);
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static DimensionArc BuildExpectedArcDimension(
        AcadSharpStyleCatalog styles,
        SheetPlan sheet,
        ArcDimensionCandidate candidate)
    {
        var center = new XYZ(
            sheet.ModelOriginX + candidate.Center.X,
            sheet.ModelOriginY + candidate.Center.Y,
            0d);
        var firstPoint = new XYZ(
            center.X + candidate.Radius * Math.Cos(candidate.StartAngleRadians),
            center.Y + candidate.Radius * Math.Sin(candidate.StartAngleRadians),
            0d);
        var secondPoint = new XYZ(
            center.X + candidate.Radius * Math.Cos(candidate.EndAngleRadians),
            center.Y + candidate.Radius * Math.Sin(candidate.EndAngleRadians),
            0d);

        return new DimensionArc
        {
            Center = center,
            FirstPoint = firstPoint,
            SecondPoint = secondPoint,
            StartAngle = candidate.StartAngleRadians,
            EndAngle = candidate.EndAngleRadians,
            DefinitionPoint = new XYZ(
                sheet.ModelOriginX + candidate.TextPoint.X,
                sheet.ModelOriginY + candidate.TextPoint.Y,
                0d),
            Text = candidate.SourceText,
            Style = styles.GetDimensionStyle(1d)
        };
    }

    private static BlockRecord BuildExpectedAxisBlock(
        CadDocument document,
        AcadSharpStyleCatalog styles)
    {
        const string name = "TEY_AXIS";
        if (document.BlockRecords.TryGetValue(name, out var existing))
            return existing;

        var block = new BlockRecord(name);
        block.Entities.Add(new Line(
            new XYZ(0d, 0d, 0d),
            new XYZ(1d, 0d, 0d))
        {
            Layer = styles.GetAnnotationLayer("PDF_ОСИ"),
            LineType = styles.GetCenterLineType(),
            LineWeight = LineWeightType.W9
        });
        document.BlockRecords.Add(block);
        return block;
    }

    private static BlockRecord BuildExpectedLevelBlock(
        CadDocument document,
        AcadSharpStyleCatalog styles)
    {
        const string name = "TEY_LEVEL";
        if (document.BlockRecords.TryGetValue(name, out var existing))
            return existing;

        var block = new BlockRecord(name);
        block.Entities.Add(new Line(
            new XYZ(0d, 0d, 0d),
            new XYZ(5d, 0d, 0d))
        {
            Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ"),
            LineWeight = LineWeightType.W9
        });
        block.Entities.Add(new LwPolyline(
            [new XY(0d, 0d), new XY(2d, 1.5d), new XY(2d, -1.5d)])
        {
            IsClosed = true,
            Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ"),
            LineWeight = LineWeightType.W9
        });
        block.Entities.Add(new AttributeDefinition
        {
            Tag = "LEVEL",
            Value = string.Empty,
            InsertPoint = new XYZ(6d, -1.25d, 0d),
            Height = 2.5d,
            Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ"),
            Style = styles.GetPdfTextStyle()
        });
        document.BlockRecords.Add(block);
        return block;
    }

    private static void AddExpected(
        IDictionary<string, ExpectedCandidate> output,
        SourceEquivalenceAssessment assessment,
        params ExpectedNativeEntity[] entities)
    {
        if (!output.TryAdd(
                assessment.CandidateId,
                new ExpectedCandidate(
                    assessment.CandidateId,
                    assessment.SemanticType,
                    entities)
                {
                    SourceEquivalenceComplete = assessment.IsComplete,
                    SourceEquivalenceReason = assessment.Reason
                }))
        {
            throw new InvalidOperationException(
                $"Duplicate independent native expectation for candidate {assessment.CandidateId}.");
        }
    }

    private static IReadOnlyDictionary<string, string> Properties(
        params (string Key, string Value)[] values)
        => values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

    private static string Point(XYZ point)
        => string.Join(",", Number(point.X), Number(point.Y), Number(point.Z));

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}
