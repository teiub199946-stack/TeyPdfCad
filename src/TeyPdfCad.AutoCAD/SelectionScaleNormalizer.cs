using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TeyPdfCad.AutoCAD;

internal sealed class SelectionScaleNormalizer
{
    public Matrix3d BuildTransform(Transaction transaction, IReadOnlyCollection<ObjectId> objectIds, double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var anchor = FindLowerLeftAnchor(transaction, objectIds);
        return Matrix3d.Scaling(scale, anchor);
    }

    public void Apply(Transaction transaction, IEnumerable<ObjectId> objectIds, Matrix3d transform)
    {
        foreach (var objectId in objectIds.Distinct())
        {
            if (objectId.IsNull || objectId.IsErased) continue;
            if (transaction.GetObject(objectId, OpenMode.ForWrite, false) is Entity entity)
                entity.TransformBy(transform);
        }
    }

    private static Point3d FindLowerLeftAnchor(Transaction transaction, IEnumerable<ObjectId> objectIds)
    {
        Extents3d? combined = null;

        foreach (var objectId in objectIds.Distinct())
        {
            if (objectId.IsNull || objectId.IsErased) continue;
            if (transaction.GetObject(objectId, OpenMode.ForRead, false) is not Entity entity) continue;

            try
            {
                var extents = entity.GeometricExtents;
                if (combined is null)
                {
                    combined = extents;
                }
                else
                {
                    var value = combined.Value;
                    value.AddExtents(extents);
                    combined = value;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Some proxy/degenerate entities do not expose geometric extents.
            }
        }

        return combined?.MinPoint ?? Point3d.Origin;
    }
}
