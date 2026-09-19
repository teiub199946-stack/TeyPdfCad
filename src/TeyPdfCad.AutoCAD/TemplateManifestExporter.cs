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
                var objectClass = entity.GetRXClass().Name;
                if (entity is AttributeDefinition attribute)
                {
                    attributes.Add(new TemplateAttributeManifest(attribute.Tag, attribute.Prompt, attribute.TextString));
                    continue;
                }

                if (!TryGetBounds(entity, out var minX, out var minY, out var maxX, out var maxY))
                {
                    unsupported.Add(objectClass);
                    continue;
                }
                entities.Add(new TemplateEntityManifest(
                    objectClass,
                    entity.Handle.ToString(),
                    minX,
                    minY,
                    maxX,
                    maxY));
            }
            blocks.Add(new TemplateBlockManifest(block.Name, entities, attributes));
        }
        return blocks.OrderBy(block => block.Name, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<TemplateStyleManifest> ReadStyles(Database database, Transaction transaction)
    {
        var styles = new List<TemplateStyleManifest>();
        AddStyleTable((TextStyleTable)transaction.GetObject(database.Textstyle, OpenMode.ForRead), transaction, styles);
        AddStyleTable((DimStyleTable)transaction.GetObject(database.Dimstyle, OpenMode.ForRead), transaction, styles);
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
}
