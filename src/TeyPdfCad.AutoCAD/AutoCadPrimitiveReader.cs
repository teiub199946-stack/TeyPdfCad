using Autodesk.AutoCAD.DatabaseServices;
using System.Globalization;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.AutoCAD;

internal sealed class AutoCadPrimitiveReader
{
    public PrimitiveScene Read(Transaction transaction, IEnumerable<ObjectId> objectIds)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        if (objectIds is null) throw new ArgumentNullException(nameof(objectIds));

        var scene = new PrimitiveScene();

        foreach (var objectId in objectIds.Distinct())
        {
            if (objectId.IsNull || objectId.IsErased) continue;
            if (transaction.GetObject(objectId, OpenMode.ForRead, false) is not Entity entity) continue;

            switch (entity)
            {
                case Line line:
                    AddLine(scene, line.StartPoint, line.EndPoint, line.Layer, SourceId(line));
                    break;

                case Polyline polyline:
                    AddPolyline(scene, polyline);
                    break;

                case DBText text:
                    scene.Texts.Add(new TextPrimitive(
                        text.TextString,
                        ToPoint2(text.Position),
                        text.Height,
                        text.Rotation,
                        text.Layer,
                        [SourceId(text)]));
                    break;

                case MText mtext:
                    scene.Texts.Add(new TextPrimitive(
                        mtext.Text,
                        ToPoint2(mtext.Location),
                        mtext.TextHeight,
                        mtext.Rotation,
                        mtext.Layer,
                        [SourceId(mtext)]));
                    break;
            }
        }

        if (TryReadSheetMetadata(out var sheet))
        {
            scene.Sheet = sheet;
            scene.TitleBlock = TitleBlockDetector.Detect(scene, sheet);
            if (scene.TitleBlock is null && scene.Texts.Count == 0)
            {
                // SHX labels can arrive as linework; keep geometry without inventing text fields.
                scene.TitleBlock = TitleBlockDetector.DetectGeometryOnly(scene, sheet);
            }
        }

        return scene;
    }

    private static bool TryReadSheetMetadata(out SheetMetadata sheet)
    {
        sheet = null!;
        if (SheetRuntimeSettings.TryGetBounds(out var runtimeBounds))
        {
            sheet = StandardSheetDetector.Detect(runtimeBounds.WidthMm, runtimeBounds.HeightMm).ToMetadata() with
            {
                PageBounds = runtimeBounds,
            };
            return true;
        }

        var widthText = Environment.GetEnvironmentVariable("TEYPDFCAD_SHEET_WIDTH_MM");
        var heightText = Environment.GetEnvironmentVariable("TEYPDFCAD_SHEET_HEIGHT_MM");
        if (!double.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 || height <= 0)
            return false;

        var detection = StandardSheetDetector.Detect(width, height);
        var minX = ParseOptionalCoordinate("TEYPDFCAD_SHEET_MIN_X");
        var minY = ParseOptionalCoordinate("TEYPDFCAD_SHEET_MIN_Y");
        sheet = detection.ToMetadata() with
        {
            PageBounds = new SheetPageBounds(minX, minY, width, height),
        };
        return true;
    }

    private static double ParseOptionalCoordinate(string variableName)
        => double.TryParse(
            Environment.GetEnvironmentVariable(variableName),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static void AddPolyline(PrimitiveScene scene, Polyline polyline)
    {
        var segmentCount = polyline.Closed ? polyline.NumberOfVertices : Math.Max(0, polyline.NumberOfVertices - 1);
        var handle = SourceId(polyline);

        for (var i = 0; i < segmentCount; i++)
        {
            var sourceId = $"{handle}#segment:{i}";
            switch (polyline.GetSegmentType(i))
            {
                case SegmentType.Line:
                {
                    var segment = polyline.GetLineSegmentAt(i);
                    AddLine(
                        scene,
                        segment.StartPoint,
                        segment.EndPoint,
                        polyline.Layer,
                        sourceId);
                    break;
                }

                case SegmentType.Arc:
                {
                    var arc = polyline.GetArcSegmentAt(i);
                    var steps = ArcTessellator.BuildSteps(arc.StartAngle, arc.EndAngle, sourceId);
                    foreach (var step in steps)
                    {
                        AddLine(
                            scene,
                            arc.EvaluatePoint(step.StartParameter),
                            arc.EvaluatePoint(step.EndParameter),
                            polyline.Layer,
                            step.SourceId);
                    }
                    break;
                }
            }
        }
    }

    private static void AddLine(
        PrimitiveScene scene,
        Autodesk.AutoCAD.Geometry.Point3d start,
        Autodesk.AutoCAD.Geometry.Point3d end,
        string? layer,
        string sourceId)
    {
        scene.Lines.Add(new LinePrimitive(
            ToPoint2(start),
            ToPoint2(end),
            layer,
            [sourceId]));
    }

    private static Point2 ToPoint2(Autodesk.AutoCAD.Geometry.Point3d point)
        => new(point.X, point.Y);

    private static string SourceId(Entity entity)
        => entity.Handle.ToString();
}
