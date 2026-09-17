using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.AutoCAD;

internal sealed class NativeDimensionWriter
{
    public IReadOnlyList<ObjectId> Write(
        Database database,
        Transaction transaction,
        BlockTableRecord targetSpace,
        IEnumerable<DimensionCandidate> candidates,
        Matrix3d coordinateTransform)
    {
        var created = new List<ObjectId>();

        foreach (var candidate in candidates)
        {
            var p1 = Transform(candidate.DefinitionPoint1, coordinateTransform);
            var p2 = Transform(candidate.DefinitionPoint2, coordinateTransform);
            var dimLinePoint = Transform(candidate.DimensionLinePoint, coordinateTransform);
            var textOverride = DimensionTextOverrideBuilder.Build(candidate.SourceText);

            Dimension dimension = candidate.Kind switch
            {
                DimensionKind.Aligned => new AlignedDimension(
                    p1,
                    p2,
                    dimLinePoint,
                    textOverride,
                    database.Dimstyle),

                DimensionKind.Rotated => new RotatedDimension(
                    Rotation(p1, p2),
                    p1,
                    p2,
                    dimLinePoint,
                    textOverride,
                    database.Dimstyle),

                _ => throw new NotSupportedException($"Unsupported dimension kind: {candidate.Kind}")
            };

            dimension.LayerId = database.Clayer;
            var objectId = targetSpace.AppendEntity(dimension);
            transaction.AddNewlyCreatedDBObject(dimension, true);
            created.Add(objectId);
        }

        return created;
    }

    private static Point3d Transform(Point2 point, Matrix3d transform)
        => new Point3d(point.X, point.Y, 0).TransformBy(transform);

    private static double Rotation(Point3d p1, Point3d p2)
        => Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
}
