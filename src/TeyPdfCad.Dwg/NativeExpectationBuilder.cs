using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
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
                            ("expectedMeasurement", Number(candidate.ReconstructedMeasurement)),
                            ("measurementTolerance", "0.000001"),
                            ("expectedDimensionText", NativeDimensionTextBuilder.Build(
                                candidate.SourceText,
                                candidate.DisplayedValue)),
                            ("expectedLayer", "PDF_РАЗМЕРЫ"),
                            ("dimensionStyleFingerprint", DwgEntityFingerprint.ComputeDimensionStyle(dimension)))));
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
        AcadSharpStyleCatalog styles,
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
        dimension.Style = styles.GetDimensionStyle(candidate.DrawingScale);
        dimension.Text = NativeDimensionTextBuilder.Build(
            candidate.SourceText,
            candidate.DisplayedValue);
        return dimension;
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

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}
