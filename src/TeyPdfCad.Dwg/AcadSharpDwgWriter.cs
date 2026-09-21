using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using TeyPdfCad.Core.Templates;

namespace TeyPdfCad.Dwg;

public sealed class AcadSharpDwgWriter
{
    public byte[] Write(
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
        TemplateLibrary? templateLibrary = null,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
        IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
        IDictionary<int, ISet<string>>? createdCandidateKeysByPage = null)
    {
        using var output = new MemoryStream();
        _ = WriteCore(
            output,
            source,
            plan,
            hatchRecognitionByPage,
            semanticRecognitionByPage,
            templateLibrary,
            templateSelectionsByPage,
            sourceReplacementPlansByPage,
            authorizedSuppressedSources: null,
            createdCandidateKeysByPage);
        return output.ToArray();
    }

    public DwgWriteResult Write(
        Stream destination,
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
        TemplateLibrary? templateLibrary = null,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
        IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
        IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null)
        => WriteCore(
            destination,
            source,
            plan,
            hatchRecognitionByPage,
            semanticRecognitionByPage,
            templateLibrary,
            templateSelectionsByPage,
            sourceReplacementPlansByPage,
            authorizedSuppressedSources,
            createdCandidateKeysByPage: null);

    private static DwgWriteResult WriteCore(
        Stream destination,
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage,
        TemplateLibrary? templateLibrary,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage,
        IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage,
        IReadOnlySet<PageSourceRef>? authorizedSuppressedSources,
        IDictionary<int, ISet<string>>? createdCandidateKeysByPage)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));

        var document = new CadDocument();
        var manifestBuilders = new Dictionary<string, CandidateManifestBuilder>(StringComparer.Ordinal);
        var sourceEmissionCounts = new Dictionary<PageSourceRef, Dictionary<string, int>>();
        var diagnosticCreatedCandidateKeyCount = 0;
        var styles = new AcadSharpStyleCatalog(document);
        var sheetsByPage = plan.Sheets.ToDictionary(sheet => sheet.PageNumber);
        var preservedBoundaryEntities = new HashSet<Entity>();
        foreach (var page in source.Pages)
        {
            var sheet = sheetsByPage[page.Number];
            var templateSourceIds = new HashSet<string>(StringComparer.Ordinal);
            if (templateLibrary is not null
                && templateSelectionsByPage is not null
                && templateSelectionsByPage.TryGetValue(page.Number, out var templateSelection)
                && templateSelection is { IsConfirmed: true, TemplateName: not null })
            {
                var template = templateLibrary.Blocks.SingleOrDefault(block =>
                    string.Equals(block.Name, templateSelection.TemplateName, StringComparison.Ordinal));
                if (template is not null)
                {
                    WriteTemplateInsert(document, styles, sheet, template);
                    templateSourceIds.UnionWith(templateSelection.SourceIdsToReplace);
                }
            }
            var hatchRecognition = hatchRecognitionByPage is not null && hatchRecognitionByPage.TryGetValue(page.Number, out var suppliedRecognition)
                ? suppliedRecognition
                : new HatchRecognizer().Recognize(page.Entities, page.Number);
            var semantics = semanticRecognitionByPage is not null && semanticRecognitionByPage.TryGetValue(page.Number, out var suppliedSemantics)
                ? suppliedSemantics
                : null;
            var replacementPlan = sourceReplacementPlansByPage is not null
                && sourceReplacementPlansByPage.TryGetValue(page.Number, out var suppliedReplacementPlan)
                    ? suppliedReplacementPlan
                    : new SourceReplacementPlanner().BuildPlan(page.Entities, semantics, hatchRecognition, page.Number);
            var authorizedForPage = authorizedSuppressedSources is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : authorizedSuppressedSources
                    .Where(sourceRef => sourceRef.PageNumber == page.Number)
                    .Select(sourceRef => sourceRef.SourceId)
                    .ToHashSet(StringComparer.Ordinal);
            var eligibleForPage = replacementPlan.EligibleSourceIds
                .ToHashSet(StringComparer.Ordinal);
            var invalidAuthorization = authorizedForPage
                .Where(sourceId => !eligibleForPage.Contains(sourceId))
                .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
                .ToArray();
            if (invalidAuthorization.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Page {page.Number} suppression authorization contains non-eligible SourceId(s): {string.Join(", ", invalidAuthorization)}.");
            }
            var suppressedSourceIds = authorizedForPage;
            var deferredCandidateKeys = replacementPlan.DeferredCandidateKeys
                .ToHashSet(StringComparer.Ordinal);
            ISet<string>? createdCandidateKeys = null;
            if (createdCandidateKeysByPage is not null)
            {
                if (!createdCandidateKeysByPage.TryGetValue(page.Number, out createdCandidateKeys))
                {
                    createdCandidateKeys = new HashSet<string>(StringComparer.Ordinal);
                    createdCandidateKeysByPage[page.Number] = createdCandidateKeys;
                }
            }
            var reviewLayersBySourceId = semantics is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : semantics.Warnings
                    .SelectMany(warning => warning.ProvenanceIds.Select(sourceId => new
                    {
                        SourceId = sourceId,
                        Layer = GetReviewLayerName(warning.Code)
                    }))
                    .GroupBy(entry => entry.SourceId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Layer, StringComparer.Ordinal);
            var patternHatches = hatchRecognition.NativeHatches
                .Where(candidate => !candidate.IsSolid)
                .Where(candidate => !deferredCandidateKeys.Contains(SourceReplacementPlanner.GetCandidateKey(candidate, page.Number)))
                .ToArray();
            var writtenBoundaries = new Dictionary<string, LwPolyline>(StringComparer.Ordinal);
            foreach (var sourceLine in page.Entities.OfType<VectorLine>())
            {
                if (templateSourceIds.Contains(sourceLine.SourceId)) continue;
                if (suppressedSourceIds.Contains(sourceLine.SourceId)) continue;
                var line = new Line(
                    new XYZ(sheet.ModelOriginX + sourceLine.Start.X, sheet.ModelOriginY + sourceLine.Start.Y, 0),
                    new XYZ(sheet.ModelOriginX + sourceLine.End.X, sheet.ModelOriginY + sourceLine.End.Y, 0));
                styles.Apply(line, GetReviewStyle(sourceLine.Style, sourceLine.SourceId, reviewLayersBySourceId));
                document.Entities.Add(line);
            }
            foreach (var sourcePolyline in page.Entities.OfType<VectorPolyline>())
            {
                if (templateSourceIds.Contains(sourcePolyline.SourceId)) continue;
                if (suppressedSourceIds.Contains(sourcePolyline.SourceId)) continue;
                if (TryGetCircle(sourcePolyline, out var center, out var radius))
                {
                    var circle = new Circle(
                        new XYZ(sheet.ModelOriginX + center.X, sheet.ModelOriginY + center.Y, 0),
                        radius);
                    styles.Apply(circle, sourcePolyline.Style);
                    document.Entities.Add(circle);
                    continue;
                }
                var polyline = new LwPolyline(sourcePolyline.Vertices.Select(vertex => new XY(
                    sheet.ModelOriginX + vertex.X,
                    sheet.ModelOriginY + vertex.Y)))
                {
                    IsClosed = sourcePolyline.IsClosed
                };
                styles.Apply(polyline, GetReviewStyle(sourcePolyline.Style, sourcePolyline.SourceId, reviewLayersBySourceId));
                document.Entities.Add(polyline);
                if (sourcePolyline.IsClosed)
                {
                    writtenBoundaries[sourcePolyline.SourceId] = polyline;
                }
            }
            foreach (var sourceFill in page.Entities.OfType<VectorFilledPath>())
            {
                var boundaries = sourceFill.Loops
                    .Where(IsValidBoundary)
                    .Select(loop => CreateBoundary(loop, sheet.ModelOriginX, sheet.ModelOriginY, sourceFill.Style, styles, document))
                    .ToArray();
                // Fill support contours are not PDF strokes. Retain them for
                // editing, but do not draw edges that were absent in the source.
                foreach (var boundary in boundaries) boundary.IsInvisible = true;
                if (boundaries.Length == 0 || !TryFindInteriorSeed(sourceFill, out var seed))
                {
                    continue;
                }

                var hatch = new Hatch
                {
                    IsSolid = true,
                    Pattern = HatchPattern.Solid,
                    SeedPoints = [new XY(
                        sheet.ModelOriginX + seed.X,
                        sheet.ModelOriginY + seed.Y)]
                };
                foreach (var boundary in boundaries)
                {
                    hatch.Paths.Add(new Hatch.BoundaryPath([boundary]));
                }
                styles.Apply(hatch, GetReviewStyle(sourceFill.Style, sourceFill.SourceId, reviewLayersBySourceId));
                document.Entities.Add(hatch);
            }
            foreach (var candidate in patternHatches)
            {
                if (candidate.PatternAngleRadians is not { } angle
                    || candidate.PatternSpacingMillimetres is not { } spacing
                    || spacing <= 0d
                    || !TryFindInteriorSeed(candidate.Boundary, [], out var seed))
                {
                    continue;
                }

                var boundary = candidate.ProvenanceIds
                    .Select(sourceId => writtenBoundaries.GetValueOrDefault(sourceId))
                    .FirstOrDefault(polyline => polyline is not null)
                    ?? CreateBoundary(candidate.Boundary, sheet.ModelOriginX, sheet.ModelOriginY, candidate.Style, styles, document);
                preservedBoundaryEntities.Add(boundary);
                var pattern = new HatchPattern("TEYPDFCAD_LINEAR");
                pattern.Lines.Add(new HatchPattern.Line
                {
                    Angle = angle,
                    BasePoint = new XY(0d, 0d),
                    Offset = new XY(-Math.Sin(angle) * spacing, Math.Cos(angle) * spacing)
                });
                var hatch = new Hatch
                {
                    IsSolid = false,
                    Pattern = pattern,
                    PatternType = HatchPatternType.Custom,
                    PatternScale = 1d,
                    SeedPoints = [new XY(sheet.ModelOriginX + seed.X, sheet.ModelOriginY + seed.Y)]
                };
                hatch.Paths.Add(new Hatch.BoundaryPath([boundary]));
                styles.Apply(hatch, candidate.Style);
                document.Entities.Add(hatch);
                createdCandidateKeys?.Add(SourceReplacementPlanner.GetCandidateKey(candidate, page.Number));
            }
            foreach (var sourceText in page.Entities.OfType<VectorText>())
            {
                if (templateSourceIds.Contains(sourceText.SourceId)) continue;
                if (suppressedSourceIds.Contains(sourceText.SourceId)) continue;
                var text = new TextEntity
                {
                    Value = sourceText.Value,
                    InsertPoint = new XYZ(
                        sheet.ModelOriginX + sourceText.InsertionPoint.X,
                        sheet.ModelOriginY + sourceText.InsertionPoint.Y,
                        0),
                    Height = sourceText.HeightPoints * VectorPdfPage.MillimetresPerPoint,
                    Rotation = sourceText.RotationRadians,
                    Style = styles.GetPdfTextStyle()
                };
                styles.Apply(text, GetReviewStyle(sourceText.Style, sourceText.SourceId, reviewLayersBySourceId));
                document.Entities.Add(text);
            }
            if (semantics is not null)
            {
                foreach (var candidate in semantics.Dimensions)
                {
                    var key = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                    if (deferredCandidateKeys.Contains(key)) continue;
                    WriteDimension(document, styles, sheet, candidate);
                    createdCandidateKeys?.Add(key);
                }
                foreach (var candidate in semantics.Leaders)
                {
                    var key = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                    if (deferredCandidateKeys.Contains(key)) continue;
                    WriteLeader(document, styles, sheet, candidate);
                    createdCandidateKeys?.Add(key);
                }
                foreach (var candidate in semantics.Axes)
                {
                    var key = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                    if (deferredCandidateKeys.Contains(key)) continue;
                    WriteAxis(document, styles, sheet, candidate);
                    createdCandidateKeys?.Add(key);
                }
                foreach (var candidate in semantics.Levels)
                {
                    var key = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                    if (deferredCandidateKeys.Contains(key)) continue;
                    WriteLevel(document, styles, sheet, candidate);
                    createdCandidateKeys?.Add(key);
                }
                foreach (var candidate in semantics.ArcDimensions)
                {
                    var key = SourceReplacementPlanner.GetCandidateKey(candidate, page.Number);
                    if (deferredCandidateKeys.Contains(key)) continue;
                    WriteArcDimension(document, styles, sheet, candidate);
                    createdCandidateKeys?.Add(key);
                }
            }
        }
        ApplyExplicitPaintOrder(document, preservedBoundaryEntities);

        var writer = new DwgWriter(destination, document)
        {
            Configuration = new DwgWriterConfiguration
            {
                CloseStream = false
            }
        };
        writer.Write();

        var manifest = new NativeWriteManifest(
            manifestBuilders
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => new ExpectedCandidate(
                        pair.Key,
                        pair.Value.SemanticType,
                        pair.Value.Entities.ToArray()),
                    StringComparer.Ordinal));
        var sourceSummary = new SourceEmissionSummary(
            sourceEmissionCounts
                .OrderBy(pair => pair.Key.PageNumber)
                .ThenBy(pair => pair.Key.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, int>)pair.Value
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)));

        return new DwgWriteResult(
            manifest,
            sourceSummary,
            diagnosticCreatedCandidateKeyCount);
    }

    private static void ApplyExplicitPaintOrder(
        CadDocument document,
        IReadOnlySet<Entity> preservedBoundaryEntities)
    {
        var ordered = PaintOrderEngine.OrderBottomToTop(document.Entities
            .Select((entity, index) => new PaintOrderItem<Entity>(
                entity,
                ResolvePaintPriority(entity, preservedBoundaryEntities),
                index,
                (entity.Layer?.Name ?? string.Empty) + "|" + entity.GetType().Name)));

        var sortTable = document.ModelSpace.CreateSortEntitiesTable();
        foreach (var entry in ordered)
            sortTable.MoveToTop(entry.Item);
    }

    private static PaintPriority ResolvePaintPriority(
        Entity entity,
        IReadOnlySet<Entity> preservedBoundaryEntities)
    {
        var layerName = entity.Layer?.Name ?? string.Empty;
        if (preservedBoundaryEntities.Contains(entity))
            return PaintPriority.PreservedBoundary;
        if (layerName.StartsWith("TEY_REVIEW", StringComparison.OrdinalIgnoreCase))
            return PaintPriority.ReviewOverlay;
        if (entity is TextEntity)
            return PaintPriority.Text;
        if (entity is Dimension)
            return PaintPriority.Dimension;
        if (entity is Leader)
            return PaintPriority.Annotation;
        if (entity is Insert insert)
        {
            if (string.Equals(insert.Block?.Name, "TEY_AXIS", StringComparison.OrdinalIgnoreCase))
                return PaintPriority.Axis;
            if (string.Equals(insert.Block?.Name, "TEY_LEVEL", StringComparison.OrdinalIgnoreCase))
                return PaintPriority.Annotation;
            return PaintPriority.BaseGeometry;
        }
        if (entity is Hatch hatch)
            return hatch.IsSolid ? PaintPriority.SolidFill : PaintPriority.PatternHatch;
        return PaintPriority.BaseGeometry;
    }

    private static void WriteDimension(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, DimensionCandidate candidate)
    {
        var first = new XYZ(sheet.ModelOriginX + candidate.DefinitionPoint1.X, sheet.ModelOriginY + candidate.DefinitionPoint1.Y, 0d);
        var second = new XYZ(sheet.ModelOriginX + candidate.DefinitionPoint2.X, sheet.ModelOriginY + candidate.DefinitionPoint2.Y, 0d);
        var dimensionPoint = new XYZ(sheet.ModelOriginX + candidate.DimensionLinePoint.X, sheet.ModelOriginY + candidate.DimensionLinePoint.Y, 0d);
        Dimension dimension = candidate.Kind == DimensionKind.Rotated
            ? new DimensionLinear
            {
                FirstPoint = first,
                SecondPoint = second,
                Rotation = candidate.RotationRadians ?? Math.Atan2(second.Y - first.Y, second.X - first.X)
            }
            : new DimensionAligned(first, second);
        dimension.DefinitionPoint = dimensionPoint;
        dimension.Style = styles.GetDimensionStyle(candidate.DrawingScale);
        dimension.Text = string.Empty;
        dimension.Layer = styles.GetAnnotationLayer("PDF_РАЗМЕРЫ");
        document.Entities.Add(dimension);
    }

    private static VectorStyle GetReviewStyle(
        VectorStyle sourceStyle,
        string sourceId,
        IReadOnlyDictionary<string, string> reviewLayersBySourceId)
        => reviewLayersBySourceId.TryGetValue(sourceId, out var reviewLayer)
            ? sourceStyle with { SourceLayer = reviewLayer }
            : sourceStyle;

    private static string GetReviewLayerName(string warningCode)
    {
        if (warningCode.StartsWith("axis-", StringComparison.Ordinal)) return "TEY_REVIEW_AXIS";
        if (warningCode.StartsWith("leader-", StringComparison.Ordinal)) return "TEY_REVIEW_LEADER";
        if (warningCode.StartsWith("level-", StringComparison.Ordinal)) return "TEY_REVIEW_LEVEL";
        if (warningCode.StartsWith("break-", StringComparison.Ordinal)) return "TEY_REVIEW_BREAK";
        if (warningCode.StartsWith("section-", StringComparison.Ordinal)) return "TEY_REVIEW_SECTION";
        if (warningCode.StartsWith("detail-", StringComparison.Ordinal)) return "TEY_REVIEW_DETAIL";
        return "TEY_REVIEW_SEMANTIC";
    }

    private static void WriteTemplateInsert(
        CadDocument document,
        AcadSharpStyleCatalog styles,
        SheetPlan sheet,
        TemplateBlockDefinition template)
    {
        if (!document.BlockRecords.TryGetValue(template.Name, out var block))
        {
            block = new BlockRecord(template.Name);
            var origin = template.EffectiveOrigin;
            foreach (var entity in template.Entities)
            {
                if (entity.ObjectClass == "AcDbLine" && entity.Points.Count == 2)
                {
                    var line = new Line(
                        new XYZ(entity.Points[0].X - origin.X, entity.Points[0].Y - origin.Y, 0d),
                        new XYZ(entity.Points[1].X - origin.X, entity.Points[1].Y - origin.Y, 0d));
                    styles.Apply(line, new VectorStyle(entity.Layer ?? "0"));
                    block.Entities.Add(line);
                }
                else if (entity.ObjectClass == "AcDbPolyline" && entity.Points.Count >= 2)
                {
                    var polyline = new LwPolyline(entity.Points.Select(point => new XY(
                        point.X - origin.X,
                        point.Y - origin.Y)))
                    {
                        IsClosed = entity.IsClosed
                    };
                    styles.Apply(polyline, new VectorStyle(entity.Layer ?? "0"));
                    block.Entities.Add(polyline);
                }
                else if (entity.ObjectClass == "AcDbCircle" && entity.Points.Count == 2)
                {
                    var center = entity.Points[0];
                    var radiusPoint = entity.Points[1];
                    var circle = new Circle(
                        new XYZ(center.X - origin.X, center.Y - origin.Y, 0d),
                        Math.Sqrt(Math.Pow(radiusPoint.X - center.X, 2d) + Math.Pow(radiusPoint.Y - center.Y, 2d)));
                    styles.Apply(circle, new VectorStyle(entity.Layer ?? "0"));
                    block.Entities.Add(circle);
                }
                else if (entity.ObjectClass == "AcDbArc"
                    && entity.Points.Count == 1
                    && entity.ArcRadius.HasValue
                    && entity.StartAngleRadians.HasValue
                    && entity.EndAngleRadians.HasValue)
                {
                    var center = entity.Points[0];
                    var arc = new Arc(
                        new XYZ(center.X - origin.X, center.Y - origin.Y, 0d),
                        entity.ArcRadius.Value,
                        entity.StartAngleRadians.Value,
                        entity.EndAngleRadians.Value);
                    styles.Apply(arc, new VectorStyle(entity.Layer ?? "0"));
                    block.Entities.Add(arc);
                }
                else if (entity.Text is not null && entity.Points.Count > 0)
                {
                    // Template MText is static reference content. Source PDF text
                    // remains authoritative until field mapping is verified, so
                    // do not duplicate it inside the inserted template block.
                }
            }
            document.BlockRecords.Add(block);
        }

        document.Entities.Add(new Insert(block)
        {
            InsertPoint = new XYZ(sheet.ModelOriginX, sheet.ModelOriginY, 0d)
        });
    }

    private static void WriteLeader(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, LeaderCandidate candidate)
    {
        var annotation = new TextEntity
        {
            Value = candidate.Text,
            InsertPoint = new XYZ(sheet.ModelOriginX + candidate.TextPoint.X, sheet.ModelOriginY + candidate.TextPoint.Y, 0d),
            Height = 2.5d,
            Layer = styles.GetAnnotationLayer("PDF_ВЫНОСКИ"),
            Style = styles.GetPdfTextStyle()
        };
        var leader = new Leader
        {
            ArrowHeadEnabled = true,
            CreationType = LeaderCreationType.CreatedWithTextAnnotation,
            PathType = LeaderPathType.StraightLineSegments,
            TextHeight = annotation.Height,
            Style = styles.GetDimensionStyle(1d),
            Layer = styles.GetAnnotationLayer("PDF_ВЫНОСКИ"),
            LineWeight = LineWeightType.W9
        };
        leader.Vertices.Add(new XYZ(sheet.ModelOriginX + candidate.ArrowPoint.X, sheet.ModelOriginY + candidate.ArrowPoint.Y, 0d));
        leader.Vertices.Add(new XYZ(sheet.ModelOriginX + candidate.TextPoint.X, sheet.ModelOriginY + candidate.TextPoint.Y, 0d));
        document.Entities.Add(annotation);
        document.Entities.Add(leader);
    }

    private static void WriteAxis(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, AxisCandidate candidate)
    {
        const string blockName = "TEY_AXIS";
        if (!document.BlockRecords.TryGetValue(blockName, out var block))
        {
            block = new BlockRecord(blockName);
            var axis = new Line(new XYZ(0d, 0d, 0d), new XYZ(1d, 0d, 0d))
            {
                Layer = styles.GetAnnotationLayer("PDF_ОСИ"),
                LineType = styles.GetCenterLineType(),
                LineWeight = LineWeightType.W9
            };
            block.Entities.Add(axis);
            document.BlockRecords.Add(block);
        }

        var dx = candidate.End.X - candidate.Start.X;
        var dy = candidate.End.Y - candidate.Start.Y;
        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(sheet.ModelOriginX + candidate.Start.X, sheet.ModelOriginY + candidate.Start.Y, 0d),
            XScale = Math.Sqrt(dx * dx + dy * dy),
            YScale = 1d,
            ZScale = 1d,
            Rotation = Math.Atan2(dy, dx),
            Layer = styles.GetAnnotationLayer("PDF_ОСИ")
        };
        document.Entities.Add(insert);
    }

    private static void WriteLevel(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, LevelCandidate candidate)
    {
        const string blockName = "TEY_LEVEL";
        if (!document.BlockRecords.TryGetValue(blockName, out var block))
        {
            block = new BlockRecord(blockName);
            var stem = new Line(new XYZ(0d, 0d, 0d), new XYZ(5d, 0d, 0d))
            {
                Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ"),
                LineWeight = LineWeightType.W9
            };
            block.Entities.Add(stem);
            var marker = new LwPolyline([new XY(0d, 0d), new XY(2d, 1.5d), new XY(2d, -1.5d)])
            {
                IsClosed = true,
                Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ"),
                LineWeight = LineWeightType.W9
            };
            block.Entities.Add(marker);
            var label = new AttributeDefinition
            {
                Tag = "LEVEL",
                Value = string.Empty,
                InsertPoint = new XYZ(6d, -1.25d, 0d),
                Height = 2.5d,
                Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ"),
                Style = styles.GetPdfTextStyle()
            };
            block.Entities.Add(label);
            document.BlockRecords.Add(block);
        }

        var dx = candidate.TextPoint.X - candidate.MarkerPoint.X;
        var dy = candidate.TextPoint.Y - candidate.MarkerPoint.Y;
        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(sheet.ModelOriginX + candidate.MarkerPoint.X, sheet.ModelOriginY + candidate.MarkerPoint.Y, 0d),
            Rotation = Math.Atan2(dy, dx),
            Layer = styles.GetAnnotationLayer("PDF_ОТМЕТКИ")
        };
        var attribute = insert.Attributes.Single();
        attribute.Value = candidate.Value;
        attribute.InsertPoint = new XYZ(sheet.ModelOriginX + candidate.TextPoint.X, sheet.ModelOriginY + candidate.TextPoint.Y, 0d);
        document.Entities.Add(insert);
    }

    private static void WriteArcDimension(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, ArcDimensionCandidate candidate)
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
        var dimension = new DimensionArc
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
            Style = styles.GetDimensionStyle(1d),
            Layer = styles.GetAnnotationLayer("PDF_РАЗМЕРЫ"),
            LineWeight = LineWeightType.W9
        };
        document.Entities.Add(dimension);
    }

    private static LwPolyline CreateBoundary(IReadOnlyList<TeyPdfCad.Core.Geometry.Point2> loop, double originX, double originY, VectorStyle style, AcadSharpStyleCatalog styles, CadDocument document)
    {
        var boundary = new LwPolyline(loop.Select(vertex => new XY(originX + vertex.X, originY + vertex.Y))) { IsClosed = true };
        styles.Apply(boundary, style);
        document.Entities.Add(boundary);
        return boundary;
    }

    private static bool IsValidBoundary(IReadOnlyList<TeyPdfCad.Core.Geometry.Point2> boundary)
        => boundary.Count >= 3 && boundary.Distinct().Count() >= 3;

    private static bool HasSameBoundary(VectorFilledPath fill, VectorPolyline polyline)
        => fill.Loops.Any(loop => loop.Count == polyline.Vertices.Count && loop.SequenceEqual(polyline.Vertices));

    private static bool TryGetCircle(VectorPolyline polyline, out TeyPdfCad.Core.Geometry.Point2 center, out double radius)
    {
        center = default;
        radius = 0d;
        if (!polyline.IsClosed || polyline.Vertices.Count < 12) return false;

        var minimumX = polyline.Vertices.Min(point => point.X);
        var maximumX = polyline.Vertices.Max(point => point.X);
        var minimumY = polyline.Vertices.Min(point => point.Y);
        var maximumY = polyline.Vertices.Max(point => point.Y);
        var width = maximumX - minimumX;
        var height = maximumY - minimumY;
        if (width <= 1e-6 || height <= 1e-6 || Math.Abs(width - height) > Math.Max(width, height) * 0.01d)
            return false;

        center = new TeyPdfCad.Core.Geometry.Point2((minimumX + maximumX) / 2d, (minimumY + maximumY) / 2d);
        radius = (width + height) / 4d;
        var candidateCenter = center;
        var candidateRadius = radius;
        var maximumRadialError = polyline.Vertices.Max(point => Math.Abs(
            Math.Sqrt(Math.Pow(point.X - candidateCenter.X, 2d) + Math.Pow(point.Y - candidateCenter.Y, 2d)) - candidateRadius));
        return maximumRadialError <= Math.Max(radius * 0.01d, 1e-5);
    }

    private static bool TryFindInteriorSeed(VectorFilledPath fill, out TeyPdfCad.Core.Geometry.Point2 seed)
        => TryFindInteriorSeed(fill.Boundary, fill.InteriorBoundaries, out seed);

    private static bool TryFindInteriorSeed(
        IReadOnlyList<TeyPdfCad.Core.Geometry.Point2> polygon,
        IReadOnlyList<IReadOnlyList<TeyPdfCad.Core.Geometry.Point2>> holes,
        out TeyPdfCad.Core.Geometry.Point2 seed)
    {
        seed = default;
        if (!IsValidBoundary(polygon)) return false;
        var minX = polygon.Min(point => point.X);
        var maxX = polygon.Max(point => point.X);
        var minY = polygon.Min(point => point.Y);
        var maxY = polygon.Max(point => point.Y);
        for (var divisions = 4; divisions <= 64; divisions *= 2)
        {
            for (var x = 1; x < divisions; x++)
            {
                for (var y = 1; y < divisions; y++)
                {
                    var candidate = new TeyPdfCad.Core.Geometry.Point2(minX + (maxX - minX) * x / divisions, minY + (maxY - minY) * y / divisions);
                    if (Contains(polygon, candidate)
                        && !IsOnBoundary(polygon, candidate)
                        && !holes.Any(hole => Contains(hole, candidate) || IsOnBoundary(hole, candidate))) { seed = candidate; return true; }
                }
            }
        }
        return false;
    }

    private static bool Contains(IReadOnlyList<TeyPdfCad.Core.Geometry.Point2> polygon, TeyPdfCad.Core.Geometry.Point2 point)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var a = polygon[current];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static bool IsOnBoundary(IReadOnlyList<TeyPdfCad.Core.Geometry.Point2> polygon, TeyPdfCad.Core.Geometry.Point2 point)
    {
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18) continue;
            var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
            if (t is >= 0d and <= 1d && Math.Abs((point.X - start.X) * dy - (point.Y - start.Y) * dx) <= 1e-9) return true;
        }
        return false;
    }
}
