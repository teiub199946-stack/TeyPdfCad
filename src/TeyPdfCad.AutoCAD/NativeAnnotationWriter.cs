using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TeyPdfCad.Core.Semantics;

namespace TeyPdfCad.AutoCAD;

internal sealed class NativeAnnotationWriter
{
    public IReadOnlyList<ObjectId> Write(
        Database database,
        Transaction transaction,
        BlockTableRecord targetSpace,
        SemanticReconstructionResult semantic,
        Matrix3d transform)
    {
        var created = new List<ObjectId>();
        EnsureLayer(database, transaction, "TEY_ОСИ");
        EnsureLayer(database, transaction, "TEY_ВЫНОСКИ");
        EnsureLayer(database, transaction, "TEY_ОТМЕТКИ");
        EnsureLayer(database, transaction, "TEY_ЗАЛИВКА");
        foreach (var axis in semantic.Axes)
        {
            var line = new Line(
                Transform(axis.Start, transform),
                Transform(axis.End, transform))
            {
                Layer = "TEY_ОСИ",
                LineWeight = LineWeight.LineWeight009
            };
            if (HasLinetype(database, transaction, "CENTER"))
                line.Linetype = "CENTER";
            created.Add(Append(targetSpace, transaction, line));
        }

        foreach (var leader in semantic.Leaders)
        {
            var mtext = new MText
            {
                Contents = leader.Text,
                Location = Transform(leader.TextPoint, transform),
                TextHeight = 2.5
            };
            var mleader = new MLeader
            {
                ContentType = ContentType.MTextContent,
                MText = mtext,
                TextLocation = mtext.Location,
                EnableDogleg = false,
                EnableLanding = false,
                LeaderLineWeight = LineWeight.LineWeight009,
                Layer = "TEY_ВЫНОСКИ"
            };
            var leaderIndex = mleader.AddLeader();
            mleader.AddFirstVertex(leaderIndex, Transform(leader.ArrowPoint, transform));
            mleader.AddLastVertex(leaderIndex, Transform(leader.TextPoint, transform));
            created.Add(Append(targetSpace, transaction, mleader));
        }

        foreach (var level in semantic.Levels)
        {
            var marker = new Line(
                Transform(level.MarkerPoint, transform),
                Transform(new TeyPdfCad.Core.Geometry.Point2(level.MarkerPoint.X + 5d, level.MarkerPoint.Y), transform))
            {
                Layer = "TEY_ОТМЕТКИ",
                LineWeight = LineWeight.LineWeight009
            };
            created.Add(Append(targetSpace, transaction, marker));

            var text = new DBText
            {
                TextString = level.Value,
                Position = Transform(level.TextPoint, transform),
                Height = 2.5,
                Layer = "TEY_ОТМЕТКИ"
            };
            created.Add(Append(targetSpace, transaction, text));
        }

        foreach (var path in semantic.NativeFillPaths)
        {
            if (path.Vertices.Count < 3)
                continue;
            var hatch = new Hatch
            {
                Layer = "TEY_ЗАЛИВКА",
                Associative = false
            };
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            var boundary = new Point2dCollection(path.Vertices
                .Select(point => Transform(point, transform))
                .Select(point => new Point2d(point.X, point.Y))
                .ToArray());
            hatch.AppendLoop(
                HatchLoopTypes.Default,
                boundary,
                new DoubleCollection(path.Vertices.Select(_ => 0d).ToArray()));
            var hatchId = Append(targetSpace, transaction, hatch);
            hatch.EvaluateHatch(true);
            created.Add(hatchId);
        }

        return created;
    }

    private static ObjectId Append<T>(BlockTableRecord targetSpace, Transaction transaction, T entity)
        where T : Entity
    {
        var objectId = targetSpace.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
        return objectId;
    }

    private static Point3d Transform(TeyPdfCad.Core.Geometry.Point2 point, Matrix3d transform)
        => new Point3d(point.X, point.Y, 0d).TransformBy(transform);

    private static void EnsureLayer(Database database, Transaction transaction, string name)
    {
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (table.Has(name))
            return;

        table.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name };
        table.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
    }

    private static bool HasLinetype(Database database, Transaction transaction, string name)
    {
        var table = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        return table.Has(name);
    }
}
