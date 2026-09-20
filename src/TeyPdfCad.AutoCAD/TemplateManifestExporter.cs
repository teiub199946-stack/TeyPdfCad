using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace TeyPdfCad.AutoCAD;

public sealed class TemplateManifestExporter
{
    [CommandMethod("TEYPDFEXPORTTEMPLATES", CommandFlags.Modal)]
    public void ExportActiveDocument()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        try
        {
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var manifest = Export(document.Database, transaction);
            var outputDirectory = Path.Combine(Path.GetTempPath(), "TeyPdfCad");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(
                outputDirectory,
                $"TemplateLibrary_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.json");
            File.WriteAllText(outputPath, manifest.ToJson(), new UTF8Encoding(false));
            transaction.Commit();
            document.Editor.WriteMessage($"\nTeyPdfCad template manifest saved: {outputPath}\n");
        }
        catch (System.Exception exception)
        {
            document.Editor.WriteMessage($"\nTeyPdfCad template export failed: {exception.Message}\n");
        }
    }

    public static TemplateLibraryManifest Export(Database database, Transaction transaction)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));

        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        var blocks = ReadBlocks(database, transaction, unsupported);
        var styles = ReadStyles(database, transaction);
        return new TemplateLibraryManifest("1", blocks, styles, unsupported.ToArray());
    }

    private static IReadOnlyList<TemplateBlockManifest> ReadBlocks(
        Database database,
        Transaction transaction,
        ISet<string> unsupported)
    {
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var blocks = new List<TemplateBlockManifest>();
        foreach (ObjectId blockId in table)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (block.IsLayout) continue;

            var entities = new List<TemplateEntityManifest>();
            var attributes = new List<TemplateAttributeManifest>();
            foreach (ObjectId entityId in block)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead, false) is not Entity entity) continue;
                if (entity is AttributeDefinition attribute)
                {
                    attributes.Add(new TemplateAttributeManifest(attribute.Tag, attribute.Prompt, attribute.TextString));
                    continue;
                }

                TemplateEntityExpansion.VisitLeaves(
                    entity,
                    IsReconstructable,
                    ExplodeSafely,
                    expandedEntity =>
                    {
                    var objectClass = expandedEntity.GetRXClass().Name;
                    if (!TryGetBounds(expandedEntity, out var minX, out var minY, out var maxX, out var maxY)
                        || !IsReconstructable(expandedEntity))
                    {
                        unsupported.Add(objectClass);
                        return;
                    }
                    entities.Add(new TemplateEntityManifest(
                        objectClass,
                        entity.Handle.ToString(),
                        minX,
                        minY,
                        maxX,
                        maxY,
                        GetGeometryPoints(expandedEntity),
                        GetText(expandedEntity),
                        GetTextHeight(expandedEntity),
                        expandedEntity.Layer,
                        GetIsClosed(expandedEntity),
                        GetRotationRadians(expandedEntity),
                        GetArcRadius(expandedEntity),
                        GetStartAngleRadians(expandedEntity),
                        GetEndAngleRadians(expandedEntity)));
                    },
                    expandedEntity => expandedEntity.Dispose());
            }
            blocks.Add(new TemplateBlockManifest(
                block.Name,
                entities,
                attributes,
                new TemplatePointManifest(block.Origin.X, block.Origin.Y)));
        }
        return blocks.OrderBy(block => block.Name, StringComparer.Ordinal).ToArray();
    }

    private static bool IsReconstructable(Entity entity)
        => entity is Line or Polyline or Circle or Arc or DBText or MText;

    private static IReadOnlyList<Entity> ExplodeSafely(Entity entity)
    {
        var children = new DBObjectCollection();
        try
        {
            entity.Explode(children);
            return children.Cast<DBObject>().OfType<Entity>().ToArray();
        }
        catch (System.Exception)
        {
            foreach (DBObject child in children) child.Dispose();
            return [];
        }
    }

    private static IReadOnlyList<TemplateStyleManifest> ReadStyles(Database database, Transaction transaction)
    {
        var styles = new List<TemplateStyleManifest>();
        AddStyleTable((TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead), transaction, styles);
        AddStyleTable((DimStyleTable)transaction.GetObject(database.DimStyleTableId, OpenMode.ForRead), transaction, styles);
        AddStyleTable((LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead), transaction, styles);
        return styles.OrderBy(style => style.ObjectClass, StringComparer.Ordinal)
            .ThenBy(style => style.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddStyleTable(SymbolTable table, Transaction transaction, ICollection<TemplateStyleManifest> output)
    {
        foreach (ObjectId id in table)
        {
            var record = (SymbolTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            output.Add(new TemplateStyleManifest(record.Name, record.GetRXClass().Name));
        }
    }

    private static bool TryGetBounds(Entity entity, out double minX, out double minY, out double maxX, out double maxY)
    {
        try
        {
            var bounds = entity.GeometricExtents;
            minX = bounds.MinPoint.X;
            minY = bounds.MinPoint.Y;
            maxX = bounds.MaxPoint.X;
            maxY = bounds.MaxPoint.Y;
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            minX = minY = maxX = maxY = 0d;
            return false;
        }
    }

    internal static IReadOnlyList<TemplatePointManifest>? GetGeometryPoints(Entity entity)
    {
        switch (entity)
        {
            case Line line:
                return [
                    new TemplatePointManifest(line.StartPoint.X, line.StartPoint.Y),
                    new TemplatePointManifest(line.EndPoint.X, line.EndPoint.Y)
                ];
            case Polyline polyline:
                var points = new List<TemplatePointManifest>(polyline.NumberOfVertices);
                for (var index = 0; index < polyline.NumberOfVertices; index++)
                {
                    var point = polyline.GetPoint2dAt(index);
                    points.Add(new TemplatePointManifest(point.X, point.Y));
                }
                return points;
            case Circle circle:
                return [
                    new TemplatePointManifest(circle.Center.X, circle.Center.Y),
                    new TemplatePointManifest(circle.Center.X + circle.Radius, circle.Center.Y)
                ];
            case Arc arc:
                return [new TemplatePointManifest(arc.Center.X, arc.Center.Y)];
            case DBText text:
                return [new TemplatePointManifest(text.Position.X, text.Position.Y)];
            case MText text:
                return [new TemplatePointManifest(text.Location.X, text.Location.Y)];
            default:
                return null;
        }
    }

    private static string? GetText(Entity entity)
        => entity switch
        {
            DBText text => text.TextString,
            MText text => text.Contents,
            _ => null
        };

    private static double? GetTextHeight(Entity entity)
        => entity switch
        {
            DBText text => text.Height,
            MText text => text.TextHeight,
            _ => null
        };

    private static bool? GetIsClosed(Entity entity)
        => entity is Polyline polyline ? polyline.Closed : null;

    private static double? GetRotationRadians(Entity entity)
        => entity switch
        {
            DBText text => text.Rotation,
            MText text => text.Rotation,
            _ => null
        };

    private static double? GetArcRadius(Entity entity)
        => entity is Arc arc ? arc.Radius : null;

    private static double? GetStartAngleRadians(Entity entity)
        => entity is Arc arc ? arc.StartAngle : null;

    private static double? GetEndAngleRadians(Entity entity)
        => entity is Arc arc ? arc.EndAngle : null;
}
