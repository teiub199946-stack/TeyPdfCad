using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using CSMath;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Dwg;

public sealed class AcadSharpDwgWriter
{
    public byte[] Write(
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        var document = new CadDocument();
        var styles = new AcadSharpStyleCatalog(document);
        foreach (var sheet in plan.Sheets)
        {
            var layout = new Layout(sheet.LayoutName)
            {
                PaperWidth = sheet.PaperWidthMillimetres,
                PaperHeight = sheet.PaperHeightMillimetres
            };
            layout.AssociatedBlock.Entities.Add(new LwPolyline([
                new XY(0, 0),
                new XY(sheet.PaperWidthMillimetres, 0),
                new XY(sheet.PaperWidthMillimetres, sheet.PaperHeightMillimetres),
                new XY(0, sheet.PaperHeightMillimetres)
            ])
            {
                IsClosed = true
            });
            layout.AddViewport(new Viewport
            {
                Center = new XYZ(
                    sheet.PaperWidthMillimetres / 2d,
                    sheet.PaperHeightMillimetres / 2d,
                    0),
                Width = sheet.PaperWidthMillimetres,
                Height = sheet.PaperHeightMillimetres,
                ViewCenter = new XY(
                    sheet.ModelOriginX + sheet.PaperWidthMillimetres / 2d,
                    sheet.ModelOriginY + sheet.PaperHeightMillimetres / 2d),
                ViewHeight = sheet.PaperHeightMillimetres,
                ViewTarget = new XYZ(
                    sheet.ModelOriginX + sheet.PaperWidthMillimetres / 2d,
                    sheet.ModelOriginY + sheet.PaperHeightMillimetres / 2d,
                    0)
            });
            document.Layouts.Add(layout);
        }
        var sheetsByPage = plan.Sheets.ToDictionary(sheet => sheet.PageNumber);
        foreach (var page in source.Pages)
        {
            var sheet = sheetsByPage[page.Number];
            var hatchRecognition = hatchRecognitionByPage is not null && hatchRecognitionByPage.TryGetValue(page.Number, out var suppliedRecognition)
                ? suppliedRecognition
                : new HatchRecognizer().Recognize(page.Entities);
            var semantics = semanticRecognitionByPage is not null && semanticRecognitionByPage.TryGetValue(page.Number, out var suppliedSemantics)
                ? suppliedSemantics
                : null;
            var consumedSemanticIds = semantics is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : semantics.Dimensions.SelectMany(candidate => candidate.ProvenanceIds)
                    .Concat(semantics.Leaders.SelectMany(candidate => candidate.ProvenanceIds))
                    .Concat(semantics.Axes.SelectMany(candidate => candidate.ProvenanceIds))
                    .ToHashSet(StringComparer.Ordinal);
            var patternHatches = hatchRecognition.NativeHatches.Where(candidate => !candidate.IsSolid).ToArray();
            var consumedPatternLineIds = patternHatches.SelectMany(candidate => candidate.ProvenanceIds).ToHashSet(StringComparer.Ordinal);
            var writtenBoundaries = new Dictionary<string, LwPolyline>(StringComparer.Ordinal);
            foreach (var sourceLine in page.Entities.OfType<VectorLine>())
            {
                if (consumedPatternLineIds.Contains(sourceLine.SourceId))
                {
                    continue;
                }
                if (consumedSemanticIds.Contains(sourceLine.SourceId)) continue;
                var line = new Line(
                    new XYZ(sheet.ModelOriginX + sourceLine.Start.X, sheet.ModelOriginY + sourceLine.Start.Y, 0),
                    new XYZ(sheet.ModelOriginX + sourceLine.End.X, sheet.ModelOriginY + sourceLine.End.Y, 0));
                styles.Apply(line, sourceLine.Style);
                document.Entities.Add(line);
            }
            foreach (var sourcePolyline in page.Entities.OfType<VectorPolyline>())
            {
                if (sourcePolyline.IsClosed && page.Entities.OfType<VectorFilledPath>().Any(fill => HasSameBoundary(fill, sourcePolyline)))
                {
                    continue;
                }
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
                styles.Apply(polyline, sourcePolyline.Style);
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
                styles.Apply(hatch, sourceFill.Style);
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
            }
            foreach (var sourceText in page.Entities.OfType<VectorText>())
            {
                if (consumedSemanticIds.Contains(sourceText.SourceId)) continue;
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
                styles.Apply(text, sourceText.Style);
                document.Entities.Add(text);
            }
            if (semantics is not null)
            {
                foreach (var candidate in semantics.Dimensions)
                    WriteDimension(document, styles, sheet, candidate);
                foreach (var candidate in semantics.Leaders)
                    WriteLeader(document, styles, sheet, candidate);
                foreach (var candidate in semantics.Axes)
                    WriteAxis(document, styles, sheet, candidate);
            }
        }
        using var output = new MemoryStream();
        using var writer = new DwgWriter(output, document);
        writer.Write();
        return output.ToArray();
    }

    private static void WriteDimension(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, DimensionCandidate candidate)
    {
        var first = new XYZ(sheet.ModelOriginX + candidate.DefinitionPoint1.X, sheet.ModelOriginY + candidate.DefinitionPoint1.Y, 0d);
        var second = new XYZ(sheet.ModelOriginX + candidate.DefinitionPoint2.X, sheet.ModelOriginY + candidate.DefinitionPoint2.Y, 0d);
        var dimensionPoint = new XYZ(sheet.ModelOriginX + candidate.DimensionLinePoint.X, sheet.ModelOriginY + candidate.DimensionLinePoint.Y, 0d);
        var dimension = new DimensionAligned(first, second)
        {
            DefinitionPoint = dimensionPoint,
            Style = styles.GetDimensionStyle(candidate.DrawingScale),
            Text = string.Empty,
            Layer = styles.GetAnnotationLayer("PDF_РАЗМЕРЫ")
        };
        document.Entities.Add(dimension);
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
            Layer = styles.GetAnnotationLayer("PDF_ВЫНОСКИ")
        };
        leader.Vertices.Add(new XYZ(sheet.ModelOriginX + candidate.ArrowPoint.X, sheet.ModelOriginY + candidate.ArrowPoint.Y, 0d));
        leader.Vertices.Add(new XYZ(sheet.ModelOriginX + candidate.TextPoint.X, sheet.ModelOriginY + candidate.TextPoint.Y, 0d));
        document.Entities.Add(annotation);
        document.Entities.Add(leader);
    }

    private static void WriteAxis(CadDocument document, AcadSharpStyleCatalog styles, SheetPlan sheet, AxisCandidate candidate)
    {
        var axis = new Line(
            new XYZ(sheet.ModelOriginX + candidate.Start.X, sheet.ModelOriginY + candidate.Start.Y, 0d),
            new XYZ(sheet.ModelOriginX + candidate.End.X, sheet.ModelOriginY + candidate.End.Y, 0d))
        {
            Layer = styles.GetAnnotationLayer("PDF_ОСИ"),
            LineType = styles.GetCenterLineType()
        };
        document.Entities.Add(axis);
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
