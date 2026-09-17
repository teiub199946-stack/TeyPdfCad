using Autodesk.AutoCAD.DatabaseServices;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.AutoCAD;

internal sealed class AutoCadPrimitiveReader
{
    public PrimitiveScene Read(Transaction transaction, IEnumerable<ObjectId> objectIds)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(objectIds);

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

        return scene;
    }

    private static void AddPolyline(PrimitiveScene scene, Polyline polyline)
    {
        var segmentCount = polyline.Closed ? polyline.NumberOfVertices : Math.Max(0, polyline.NumberOfVertices - 1);
        var handle = SourceId(polyline);

        for (var i = 0; i < segmentCount; i++)
        {
            if (polyline.GetSegmentType(i) != SegmentType.Line) continue;

            var segment = polyline.GetLineSegmentAt(i);
            AddLine(
                scene,
                segment.StartPoint,
                segment.EndPoint,
                polyline.Layer,
                $"{handle}#segment:{i}");
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
