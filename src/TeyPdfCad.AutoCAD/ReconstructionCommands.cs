using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using TeyPdfCad.Core.Bridge;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.AutoCAD;

public sealed class ReconstructionCommands
{
    [CommandMethod("TEYPDFPING", CommandFlags.Modal)]
    public void Ping()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        document?.Editor.WriteMessage("\nTeyPdfCad PING OK. Plugin commands are registered.\n");
    }

    [CommandMethod("TEYPDFANALYZE", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AnalyzeSelectedPdfImportObjects()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var editor = document.Editor;
        var objectIds = GetPdfImportSelection(editor);
        if (objectIds is null) return;

        using var transaction = document.Database.TransactionManager.StartTransaction();
        var scene = new AutoCadPrimitiveReader().Read(transaction, objectIds);
        var semantic = new SemanticReconstructionEngine().Analyze(scene);

        WriteAnalysisReport(editor, objectIds.Length, scene.Lines.Count, scene.Texts.Count, semantic);
        editor.WriteMessage($"\n{DrawingUnitDiagnostics.Format(document.Database.Insunits)}\n");
        // No commit: analysis is intentionally read-only.
    }

    [CommandMethod("TEYPDFDUMP", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void DumpSelectedPdfImportObjects()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var editor = document.Editor;
        var database = document.Database;
        var objectIds = GetPdfImportSelection(editor);
        if (objectIds is null) return;

        using var transaction = database.TransactionManager.StartTransaction();
        var scene = new AutoCadPrimitiveReader().Read(transaction, objectIds);
        var fixture = PrimitiveSceneFixtureFormatter.Format(
            scene,
            objectIds.Length,
            (int)database.Insunits);

        try
        {
            var outputDirectory = Path.Combine(Path.GetTempPath(), "TeyPdfCad");
            Directory.CreateDirectory(outputDirectory);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
            var outputPath = Path.Combine(outputDirectory, $"PrimitiveScene_{timestamp}.json");
            File.WriteAllText(outputPath, fixture, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            editor.WriteMessage(
                $"\nTeyPdfCad fixture saved: {outputPath}\n" +
                $"selected={objectIds.Length}, lines={scene.Lines.Count}, texts={scene.Texts.Count}, " +
                $"INSUNITS={(int)database.Insunits}. Drawing was not changed.\n");
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nTeyPdfCad fixture export failed: {ex.Message}\n");
        }

        // No commit: fixture capture is intentionally read-only.
    }

    [CommandMethod("TEYPDFDUMPALL", CommandFlags.Modal)]
    public void DumpAllModelSpaceObjects()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var objectIds = GetAllModelSpaceObjects(document.Database);
        if (objectIds.Length == 0)
        {
            document.Editor.WriteMessage("\nTeyPdfCad: ModelSpace is empty.");
            return;
        }

        DumpFixture(document, objectIds);
    }

    [CommandMethod("TEYPDFRECONSTRUCT", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ReconstructSelectedPdfImportObjects()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var editor = document.Editor;
        var database = document.Database;
        var objectIds = GetPdfImportSelection(editor);
        if (objectIds is null) return;

        Reconstruct(document, objectIds);
    }

    [CommandMethod("TEYPDFRECONSTRUCTALL", CommandFlags.Modal)]
    public void ReconstructAllModelSpaceObjects()
    {
        if (!BridgeRuntimeSettings.TryReadFromEnvironment(out var settings, out var settingsError))
        {
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\nTeyPdfCad bridge settings rejected: {settingsError}");
            return;
        }

        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            settings?.Fail("No active AutoCAD document.");
            return;
        }

        var objectIds = GetAllModelSpaceObjects(document.Database);
        if (objectIds.Length == 0)
        {
            document.Editor.WriteMessage("\nTeyPdfCad: ModelSpace is empty. Drawing was not changed.");
            settings?.Fail("ModelSpace is empty.");
            return;
        }

        Reconstruct(document, objectIds, settings);
    }

    [CommandMethod("TEYPDFBRIDGECOMPLETE", CommandFlags.Modal)]
    public void CompleteBridgeAfterSave()
    {
        var statusPath = Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.StatusFile);
        if (!string.IsNullOrWhiteSpace(statusPath))
            BridgeRuntimeSettings.CompleteSavedOutput(statusPath);
    }

    [CommandMethod("TEYPDFSHEETAUDIT", CommandFlags.Modal)]
    public void AuditSheetLayout()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var editor = document.Editor;
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var layoutManager = LayoutManager.Current;
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var found = false;

        foreach (var orientation in new[] { SheetOrientation.Landscape, SheetOrientation.Portrait })
        {
            var layoutName = $"TeyPdfCad_A3_{orientation}";
            ObjectId layoutId;
            try
            {
                layoutId = layoutManager.GetLayoutId(layoutName);
            }
            catch (System.Exception)
            {
                continue;
            }

            if (layoutId.IsNull) continue;
            found = true;
            var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForRead);
            var paperSpace = (BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            var paperEntities = paperSpace.Cast<ObjectId>()
                .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
                .OfType<Entity>()
                .ToArray();
            var blockName = $"TeyPdfCad_TitleBlock_{orientation}";
            var blockLineCount = 0;
            var blockTextCount = 0;
            var blockLineWeights = new HashSet<short>();
            if (blockTable.Has(blockName))
            {
                var definition = (BlockTableRecord)transaction.GetObject(blockTable[blockName], OpenMode.ForRead);
                foreach (var id in definition)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false) is Line line)
                    {
                        blockLineCount++;
                        blockLineWeights.Add((short)line.LineWeight);
                    }
                    else if (transaction.GetObject(id, OpenMode.ForRead, false) is DBText or MText)
                    {
                        blockTextCount++;
                    }
                }
            }

            editor.WriteMessage(
                $"\nTeyPdfCad sheet audit: layout={layoutName}, " +
                $"media='{layout.CanonicalMediaName}', paperEntities={paperEntities.Length}, " +
                $"titleBlock={blockName}, blockLines={blockLineCount}, blockTexts={blockTextCount}, " +
                $"lineweights=[{string.Join(",", blockLineWeights.OrderBy(value => value))}].");
        }

        if (!found)
            editor.WriteMessage("\nTeyPdfCad sheet audit: no TeyPdfCad A3 layout found.");

        transaction.Commit();
    }

    [CommandMethod("TEYPDFAUDITDWG", CommandFlags.Modal)]
    public void AuditConvertedDwg()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
        var layoutCount = 0;
        var viewportCount = 0;
        foreach (DBDictionaryEntry entry in layouts)
        {
            var layout = (Layout)transaction.GetObject(entry.Value, OpenMode.ForRead);
            if (layout.ModelType || !layout.LayoutName.StartsWith("Лист-", StringComparison.Ordinal)) continue;
            layoutCount++;
            var paperSpace = (BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            viewportCount += paperSpace.Cast<ObjectId>()
                .Count(id => transaction.GetObject(id, OpenMode.ForRead, false) is Viewport);
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var entities = modelSpace.Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<Entity>()
            .ToArray();
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var lineTypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        var snapshot = new DwgAcceptanceSnapshot(
            layoutCount,
            entities.Length,
            viewportCount,
            layerTable.Cast<ObjectId>().Count(),
            lineTypeTable.Cast<ObjectId>().Count(),
            entities.Count(entity => entity is Hatch),
            entities.Count(entity => entity is Dimension),
            entities.Count(entity => entity is Leader or MLeader));
        document.Editor.WriteMessage("\nTEYPDFCAD_AUDIT " + DwgAcceptanceAudit.Format(snapshot) + "\n");
        transaction.Commit();
    }

    [CommandMethod("TEYPDFDIMMETRICS", CommandFlags.Modal)]
    public void ExportDimensionTextMetrics()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var database = document.Database;
        var editor = document.Editor;
        var metrics = new List<DimensionMetric>();

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId,
                OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForRead);

            foreach (var objectId in modelSpace.Cast<ObjectId>())
            {
                if (transaction.GetObject(objectId, OpenMode.ForRead, false) is not Dimension dimension)
                    continue;

                var textMetrics = new List<DimensionTextMetric>();
                var blockGeometry = new List<DimensionBlockGeometryMetric>();
                var explodedGeometry = new List<DimensionBlockGeometryMetric>();
                var explodedTextMetrics = new List<DimensionTextMetric>();
                string? error = null;
                var dimBlockHandle = string.Empty;
                var candidateIdentity = ReadCandidateIdentity(dimension);

                try
                {
                    dimension.UpgradeOpen();
                    dimension.RecomputeDimensionBlock(true);

                    if (dimension.DimBlockId.IsNull)
                    {
                        error = "AutoCAD did not provide a dimension display block after recompute.";
                    }
                    else
                    {
                        dimBlockHandle = dimension.DimBlockId.Handle.ToString();
                        var dimBlock = (BlockTableRecord)transaction.GetObject(
                            dimension.DimBlockId,
                            OpenMode.ForRead);

                        foreach (var entityId in dimBlock.Cast<ObjectId>())
                        {
                            var entity = transaction.GetObject(
                                entityId,
                                OpenMode.ForRead,
                                false);

                            if (entity is MText mtext)
                            {
                                var style = ReadTextStyle(transaction, database, mtext.TextStyleId);
                                var fragments = ReadMTextFragments(mtext);
                                textMetrics.Add(new DimensionTextMetric(
                                    "MText",
                                    mtext.Handle.ToString(),
                                    mtext.Contents ?? string.Empty,
                                    "mtext-actual-bounds-dimblock-mcs",
                                    mtext.ActualWidth,
                                    mtext.ActualHeight,
                                    mtext.Rotation,
                                    mtext.Location.X,
                                    mtext.Location.Y,
                                    mtext.Location.Z,
                                    style.Name,
                                    style.FontFile,
                                    style.WidthFactor,
                                    style.FontResolvedPath,
                                    style.FontSha256,
                                    mtext.TextHeight,
                                    style.WidthFactor,
                                    BackgroundFill: mtext.BackgroundFill,
                                    UseBackgroundColor: mtext.UseBackgroundColor,
                                    BackgroundScaleFactor: mtext.BackgroundFill
                                        ? mtext.BackgroundScaleFactor
                                        : 0d,
                                    ShowBorders: mtext.ShowBorders,
                                    Attachment: mtext.Attachment.ToString(),
                                    Fragments: fragments));
                            }
                            else if (entity is DBText dbText)
                            {
                                var style = ReadTextStyle(transaction, database, dbText.TextStyleId);
                                var extents = dbText.GeometricExtents;
                                textMetrics.Add(new DimensionTextMetric(
                                    "DBText",
                                    dbText.Handle.ToString(),
                                    dbText.TextString ?? string.Empty,
                                    "dbtext-geometric-extents-dimblock-mcs",
                                    Math.Abs(extents.MaxPoint.X - extents.MinPoint.X),
                                    Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y),
                                    dbText.Rotation,
                                    dbText.Position.X,
                                    dbText.Position.Y,
                                    dbText.Position.Z,
                                    style.Name,
                                    style.FontFile,
                                    style.WidthFactor,
                                    style.FontResolvedPath,
                                    style.FontSha256,
                                    dbText.Height,
                                    dbText.WidthFactor));
                            }
                            else if (entity is Entity geometryEntity)
                            {
                                blockGeometry.Add(ReadDimensionBlockGeometry(
                                    transaction,
                                    geometryEntity));
                            }
                        }

                        var explodedEvidence = ReadExplodedDimensionEvidence(
                            transaction,
                            database,
                            dimension);
                        explodedGeometry.AddRange(explodedEvidence.Geometry);
                        explodedTextMetrics.AddRange(explodedEvidence.TextMetrics);

                        if (textMetrics.Count == 0)
                        {
                            error = "Dimension display block contains no MText/DBText entity; rendered text metrics are unavailable.";
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                }

                metrics.Add(new DimensionMetric(
                    dimension.Handle.ToString(),
                    dimension.GetType().Name,
                    dimension.Measurement,
                    dimension.DimensionText ?? string.Empty,
                    dimBlockHandle,
                    textMetrics,
                    error,
                    candidateIdentity.CandidateId,
                    candidateIdentity.Role,
                    blockGeometry,
                    explodedGeometry,
                    explodedTextMetrics));
            }

            // RecomputeDimensionBlock can update the anonymous display block.
            // This command is diagnostic only: abort the transaction so the
            // user's drawing is never modified by metrics capture.
            transaction.Abort();
        }

        var report = new DimensionMetricsReport(
            DimensionMetricsReportFormatter.SchemaVersion,
            document.Name ?? string.Empty,
            database.Insunits.ToString(),
            metrics);

        try
        {
            var configuredOutput = Environment.GetEnvironmentVariable(
                "TEYPDFCAD_DIM_METRICS_OUTPUT");
            string outputPath;
            if (!string.IsNullOrWhiteSpace(configuredOutput))
            {
                outputPath = Path.GetFullPath(configuredOutput);
                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                    throw new InvalidOperationException(
                        "Configured dimension metrics output has no directory.");
                Directory.CreateDirectory(outputDirectory);
            }
            else
            {
                var drawingPath = document.Name;
                var drawingDirectory = !string.IsNullOrWhiteSpace(drawingPath)
                    ? Path.GetDirectoryName(drawingPath)
                    : null;
                var drawingBaseName = !string.IsNullOrWhiteSpace(drawingPath)
                    ? Path.GetFileNameWithoutExtension(drawingPath)
                    : null;

                if (!string.IsNullOrWhiteSpace(drawingDirectory)
                    && !string.IsNullOrWhiteSpace(drawingBaseName)
                    && Directory.Exists(drawingDirectory))
                {
                    outputPath = Path.Combine(
                        drawingDirectory,
                        drawingBaseName + ".dimension-metrics.json");
                }
                else
                {
                    var outputDirectory = Path.Combine(Path.GetTempPath(), "TeyPdfCad");
                    Directory.CreateDirectory(outputDirectory);
                    var timestamp = DateTime.UtcNow.ToString(
                        "yyyyMMdd_HHmmssfff",
                        CultureInfo.InvariantCulture);
                    outputPath = Path.Combine(
                        outputDirectory,
                        $"DimensionMetrics_{timestamp}.json");
                }
            }

            File.WriteAllText(
                outputPath,
                DimensionMetricsReportFormatter.Format(report),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            editor.WriteMessage(
                $"\nTEYPDFCAD_DIM_METRICS {outputPath}\n" +
                $"dimensions={metrics.Count}, " +
                $"textEntities={metrics.Sum(item => item.TextMetrics.Count)}, " +
                $"errors={metrics.Count(item => !string.IsNullOrWhiteSpace(item.Error))}. " +
                "Drawing was not changed.\n");
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage(
                $"\nTeyPdfCad dimension metrics export failed: {ex.Message}\n");
        }
    }

    private const string NativeCandidateAppId = "TEYCONVERT_CANDIDATE_V1";

    private static (string CandidateId, string Role) ReadCandidateIdentity(
        Dimension dimension)
    {
        try
        {
            using var xdata = dimension.GetXDataForApplication(NativeCandidateAppId);
            if (xdata is null)
                return (string.Empty, string.Empty);

            var values = xdata.AsArray()
                .Where(value => value.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                .Select(value => value.Value as string)
                .ToArray();

            if (values.Length != 2
                || string.IsNullOrWhiteSpace(values[0])
                || string.IsNullOrWhiteSpace(values[1]))
            {
                return (string.Empty, string.Empty);
            }

            return (values[0]!, values[1]!);
        }
        catch (System.Exception)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static DimensionBlockGeometryMetric ReadDimensionBlockGeometry(
        Transaction transaction,
        Entity entity,
        string? identityOverride = null)
    {
        double? startX = null;
        double? startY = null;
        double? startZ = null;
        double? endX = null;
        double? endY = null;
        double? endZ = null;
        double? minX = null;
        double? minY = null;
        double? minZ = null;
        double? maxX = null;
        double? maxY = null;
        double? maxZ = null;
        var nestedBlockName = string.Empty;
        var vertexCount = 0;
        var geometryKind = "other";

        if (entity is Line line)
        {
            geometryKind = "line";
            startX = line.StartPoint.X;
            startY = line.StartPoint.Y;
            startZ = line.StartPoint.Z;
            endX = line.EndPoint.X;
            endY = line.EndPoint.Y;
            endZ = line.EndPoint.Z;
        }
        else if (entity is Polyline polyline)
        {
            geometryKind = "polyline";
            vertexCount = polyline.NumberOfVertices;
        }
        else if (entity is Solid)
        {
            geometryKind = "solid";
            vertexCount = 4;
        }
        else if (entity is Arc)
        {
            geometryKind = "arc";
        }
        else if (entity is BlockReference blockReference)
        {
            geometryKind = "block-reference";
            if (!blockReference.BlockTableRecord.IsNull
                && transaction.GetObject(
                    blockReference.BlockTableRecord,
                    OpenMode.ForRead,
                    false) is BlockTableRecord nested)
            {
                nestedBlockName = nested.Name ?? string.Empty;
            }
        }

        try
        {
            var extents = entity.GeometricExtents;
            minX = extents.MinPoint.X;
            minY = extents.MinPoint.Y;
            minZ = extents.MinPoint.Z;
            maxX = extents.MaxPoint.X;
            maxY = extents.MaxPoint.Y;
            maxZ = extents.MaxPoint.Z;
        }
        catch (System.Exception)
        {
            // Some generated entities may not expose valid extents. Keep the
            // type/handle evidence and leave extents null; the comparator will
            // fail closed rather than invent geometry.
        }

        return new DimensionBlockGeometryMetric(
            entity.GetType().Name,
            identityOverride ?? SafeEntityIdentity(entity),
            geometryKind,
            startX,
            startY,
            startZ,
            endX,
            endY,
            endZ,
            minX,
            minY,
            minZ,
            maxX,
            maxY,
            maxZ,
            nestedBlockName,
            vertexCount,
            SafeColorMethod(entity, out var rgbColor),
            rgbColor,
            entity.LineWeight.ToString(),
            (int)entity.LineWeight >= 0 ? (int)entity.LineWeight : null,
            SafeEntityLinetype(entity),
            SafeEntityLayer(entity));
    }

    private static (
        IReadOnlyList<DimensionBlockGeometryMetric> Geometry,
        IReadOnlyList<DimensionTextMetric> TextMetrics) ReadExplodedDimensionEvidence(
        Transaction transaction,
        Database database,
        Dimension dimension)
    {
        var geometry = new List<DimensionBlockGeometryMetric>();
        var textMetrics = new List<DimensionTextMetric>();
        var exploded = new DBObjectCollection();

        try
        {
            dimension.Explode(exploded);
            var ordinal = 0;
            foreach (DBObject item in exploded)
            {
                if (item is not Entity entity)
                    continue;

                var identity = "explode-" + ordinal.ToString(CultureInfo.InvariantCulture);
                ordinal++;

                if (entity is MText mtext)
                {
                    var style = ReadTextStyle(
                        transaction,
                        database,
                        mtext.TextStyleId);
                    textMetrics.Add(new DimensionTextMetric(
                        "MText",
                        identity,
                        mtext.Contents ?? string.Empty,
                        "mtext-actual-bounds-exploded-wcs",
                        mtext.ActualWidth,
                        mtext.ActualHeight,
                        mtext.Rotation,
                        mtext.Location.X,
                        mtext.Location.Y,
                        mtext.Location.Z,
                        style.Name,
                        style.FontFile,
                        style.WidthFactor,
                        style.FontResolvedPath,
                        style.FontSha256,
                        mtext.TextHeight,
                        style.WidthFactor,
                        BackgroundFill: mtext.BackgroundFill,
                        UseBackgroundColor: mtext.UseBackgroundColor,
                        BackgroundScaleFactor: mtext.BackgroundFill
                            ? mtext.BackgroundScaleFactor
                            : 0d,
                        ShowBorders: mtext.ShowBorders,
                        Attachment: mtext.Attachment.ToString(),
                        Fragments: ReadMTextFragments(mtext)));
                    continue;
                }

                if (entity is DBText dbText)
                {
                    var style = ReadTextStyle(
                        transaction,
                        database,
                        dbText.TextStyleId);
                    var extents = dbText.GeometricExtents;
                    textMetrics.Add(new DimensionTextMetric(
                        "DBText",
                        identity,
                        dbText.TextString ?? string.Empty,
                        "dbtext-geometric-extents-exploded-wcs",
                        Math.Abs(extents.MaxPoint.X - extents.MinPoint.X),
                        Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y),
                        dbText.Rotation,
                        dbText.Position.X,
                        dbText.Position.Y,
                        dbText.Position.Z,
                        style.Name,
                        style.FontFile,
                        style.WidthFactor,
                        style.FontResolvedPath,
                        style.FontSha256,
                        dbText.Height,
                        dbText.WidthFactor));
                    continue;
                }

                geometry.Add(ReadDimensionBlockGeometry(
                    transaction,
                    entity,
                    identity));
            }

            return (geometry, textMetrics);
        }
        catch (System.Exception)
        {
            return ([], []);
        }
        finally
        {
            foreach (DBObject item in exploded)
                item.Dispose();
        }
    }

    private static string SafeColorMethod(
        Entity entity,
        out int? rgbColor)
    {
        rgbColor = null;
        try
        {
            var color = entity.Color;
            var method = color.ColorMethod.ToString();
            if (string.Equals(method, "ByColor", StringComparison.Ordinal)
                || string.Equals(method, "ByAci", StringComparison.Ordinal))
            {
                rgbColor =
                    (color.Red << 16)
                    | (color.Green << 8)
                    | color.Blue;
            }

            return method;
        }
        catch (System.Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeEntityLinetype(Entity entity)
    {
        try
        {
            return entity.Linetype ?? string.Empty;
        }
        catch (System.Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeEntityLayer(Entity entity)
    {
        try
        {
            return entity.Layer ?? string.Empty;
        }
        catch (System.Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeEntityIdentity(Entity entity)
    {
        try
        {
            return entity.Handle.ToString();
        }
        catch (System.Exception)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<DimensionTextFragmentMetric> ReadMTextFragments(
        MText mtext)
    {
        var fragments = new List<DimensionTextFragmentMetric>();
        try
        {
            MTextFragmentCallback callback = (fragment, _) =>
            {
                var extents = fragment.Extents;
                var location = fragment.Location;
                var direction = fragment.Direction;
                fragments.Add(new DimensionTextFragmentMetric(
                    fragment.Text ?? string.Empty,
                    Convert.ToString(fragment.TrueTypeFont, CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    Convert.ToString(fragment.ShxFont, CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    extents.X,
                    extents.Y,
                    fragment.CapsHeight,
                    fragment.TrackingFactor,
                    fragment.WidthFactor,
                    fragment.ObliqueAngle,
                    location.X,
                    location.Y,
                    location.Z,
                    direction.X,
                    direction.Y,
                    direction.Z,
                    fragment.Bold,
                    fragment.Italic,
                    fragment.StackTop,
                    fragment.StackBottom,
                    fragment.Underlined,
                    fragment.Overlined,
                    fragment.Strikethrough));
                return MTextFragmentCallbackStatus.Continue;
            };

            mtext.ExplodeFragments(callback);
        }
        catch (System.Exception)
        {
            // Fragment evidence is diagnostic-only and fail-closed. If AutoCAD
            // cannot enumerate the generated MText layout we leave the list
            // empty; the independent comparator will reject that candidate.
            return [];
        }

        return fragments;
    }

    private static (
        string Name,
        string FontFile,
        double WidthFactor,
        string FontResolvedPath,
        string FontSha256) ReadTextStyle(
        Transaction transaction,
        Database database,
        ObjectId textStyleId)
    {
        if (textStyleId.IsNull
            || transaction.GetObject(
                textStyleId,
                OpenMode.ForRead,
                false) is not TextStyleTableRecord style)
        {
            return (string.Empty, string.Empty, 1d, string.Empty, string.Empty);
        }

        var fontFile = style.FileName ?? string.Empty;
        var resolvedPath = ResolveFontPath(database, fontFile);
        return (
            style.Name ?? string.Empty,
            fontFile,
            style.XScale,
            resolvedPath,
            DimensionMetricsFileIdentity.ComputeSha256(resolvedPath));
    }

    private static string ResolveFontPath(Database database, string fontFile)
    {
        if (string.IsNullOrWhiteSpace(fontFile))
            return string.Empty;

        try
        {
            var path = HostApplicationServices.Current.FindFile(
                fontFile,
                database,
                FindFileHint.FontFile);
            return File.Exists(path) ? Path.GetFullPath(path) : string.Empty;
        }
        catch (System.Exception)
        {
            return string.Empty;
        }
    }

    [CommandMethod("TEYPDFSHEETCONFIG", CommandFlags.Modal)]
    public void ConfigureSheetBounds()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var editor = document.Editor;
        var width = PromptPositiveDouble(editor, "\nMeasured page width in mm: ");
        if (width is null) return;
        var height = PromptPositiveDouble(editor, "\nMeasured page height in mm: ");
        if (height is null) return;
        var drawingUnitsPerMm = PromptPositiveDouble(
            editor,
            "\nDrawing units per mm <1>: ",
            defaultValue: 1);
        if (drawingUnitsPerMm is null) return;
        var minX = PromptDouble(editor, "\nPage MinX in drawing units <0>: ") ?? 0;
        var minY = PromptDouble(editor, "\nPage MinY in drawing units <0>: ") ?? 0;

        var bounds = new SheetPageBounds(
            minX,
            minY,
            width.Value,
            height.Value,
            DrawingUnitsPerMm: drawingUnitsPerMm.Value);
        SheetRuntimeSettings.SetBounds(bounds);
        var detection = StandardSheetDetector.Detect(bounds.WidthMm, bounds.HeightMm);
        editor.WriteMessage(
            $"\nTeyPdfCad sheet bounds configured: {bounds.WidthMm:G8} x {bounds.HeightMm:G8} mm, " +
            $"scale={bounds.DrawingUnitsPerMm:G8} drawing units/mm, " +
            $"drawing extents=({bounds.MinX:G8},{bounds.MinY:G8})..({bounds.MaxX:G8},{bounds.MaxY:G8}), " +
            $"format={detection.Format}, orientation={detection.Orientation}. " +
            "Drawing was not changed.");
    }

    private static double? PromptPositiveDouble(Editor editor, string message, double? defaultValue = null)
    {
        var options = new PromptDoubleOptions(message)
        {
            AllowNone = defaultValue.HasValue,
        };
        if (defaultValue.HasValue)
            options.DefaultValue = defaultValue.Value;

        var result = editor.GetDouble(options);
        if (result.Status == PromptStatus.None && defaultValue.HasValue)
            return defaultValue.Value;
        return result.Status == PromptStatus.OK && result.Value > 0 ? result.Value : null;
    }

    private static double? PromptDouble(Editor editor, string message)
    {
        var result = editor.GetDouble(new PromptDoubleOptions(message) { AllowNone = true });
        return result.Status == PromptStatus.OK ? result.Value : null;
    }

    private static void Reconstruct(
        Document document,
        ObjectId[] objectIds,
        BridgeRuntimeSettings? settings = null)
    {
        var editor = document.Editor;
        var database = document.Database;

        editor.WriteMessage($"\n{DrawingUnitDiagnostics.Format(database.Insunits)}\n");

        using var transaction = database.TransactionManager.StartTransaction();

        var scene = new AutoCadPrimitiveReader().Read(transaction, objectIds);
        var semantic = new SemanticReconstructionEngine().Analyze(
            scene,
            settings?.RecognitionOptions);

        SheetWriteResult? sheetResult = null;
        if (scene.Sheet is { } sheet && scene.TitleBlock is { IsCandidate: true } titleBlock)
        {
            try
            {
                sheetResult = new SheetLayoutWriter().Write(database, transaction, sheet, titleBlock);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage(
                    $"\nTeyPdfCad: sheet layout/title block creation failed. {ex.Message} Transaction rolled back.");
                settings?.Fail($"Sheet layout/title block creation failed: {ex.Message}");
                return;
            }
        }

        var hasNativeAnnotations = semantic.Dimensions.Count > 0
            || semantic.Axes.Count > 0
            || semantic.Leaders.Count > 0
            || semantic.Levels.Count > 0
            || semantic.NativeFillPaths.Count > 0;
        if (!hasNativeAnnotations)
        {
            if (sheetResult is null)
            {
                editor.WriteMessage("\nTeyPdfCad: no validated linear dimensions or sheet candidate were reconstructed. Drawing was not changed.");
                settings?.Fail("No validated linear dimensions or sheet candidate were reconstructed.");
                return;
            }

            if (settings is not null)
                settings.ApplyOutputUnits(database);

            transaction.Commit();
            editor.Regen();
            editor.WriteMessage(
                $"\nTeyPdfCad: created sheet layout={sheetResult.LayoutName}, " +
                $"title block={sheetResult.BlockName}; no validated linear dimensions were found." +
                (sheetResult.PlotWarning is null ? string.Empty : $" Plot warning: {sheetResult.PlotWarning}"));
            return;
        }

        var transform = Autodesk.AutoCAD.Geometry.Matrix3d.Identity;
        SelectionScaleNormalizer? normalizer = null;
        if (semantic.Dimensions.Count > 0 && semantic.DetectedDrawingScales.Count == 1)
        {
            normalizer = new SelectionScaleNormalizer();
            transform = normalizer.BuildTransform(transaction, objectIds, semantic.DetectedDrawingScales[0]);

            // Scaling the imported geometry is essential: the resulting native AutoCAD Dimension.Measurement
            // must equal the real drawing distance instead of merely displaying an overridden label.
            normalizer.Apply(transaction, objectIds, transform);
        }
        else if (semantic.Dimensions.Count > 0)
        {
            editor.WriteMessage(
                $"\nTeyPdfCad: {semantic.DetectedDrawingScales.Count} drawing scale groups detected; " +
                "linear dimensions remain source geometry, independent annotations are still reconstructed.");
        }

        var currentSpace = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
        var createdIds = normalizer is null
            ? Array.Empty<ObjectId>()
            : new NativeDimensionWriter().Write(
                database,
                transaction,
                currentSpace,
                semantic.Dimensions,
                transform);
        var annotationIds = new NativeAnnotationWriter().Write(
            database,
            transaction,
            currentSpace,
            semantic,
            transform);

        var validation = normalizer is null
            ? new NativeDimensionValidationResult(true, 0d, null)
            : new NativeDimensionValidator().Validate(
                transaction,
                createdIds,
                semantic.Dimensions);

        if (!validation.Success)
        {
            editor.WriteMessage($"\nTeyPdfCad: native dimension validation failed. {validation.ErrorMessage} Transaction rolled back.");
            settings?.Fail($"Native dimension validation failed: {validation.ErrorMessage}");
            return;
        }

        if (settings is not null)
        {
            if (!settings.PreserveSourceGeometry)
                EraseSourceGeometry(transaction, objectIds,
                    normalizer is null
                        ? Array.Empty<string>()
                        : semantic.Dimensions.SelectMany(candidate => candidate.ProvenanceIds));
            settings.ApplyOutputUnits(database);
        }

        transaction.Commit();
        editor.Regen();

        editor.WriteMessage(
            $"\nTeyPdfCad: reconstructed {createdIds.Count} native dimensions and {annotationIds.Count} native annotation entities. " +
            $"Scale={(normalizer is null ? "not-global" : semantic.DetectedDrawingScales[0].ToString("G8"))}; max native measurement error={validation.MaxRelativeError:P4}. " +
            (sheetResult is null
                ? string.Empty
                : $" Sheet layout={sheetResult.LayoutName}, title block={sheetResult.BlockName}. " +
                  (sheetResult.PlotWarning is null ? string.Empty : $"Plot warning: {sheetResult.PlotWarning} ")) +
            (settings?.PreserveSourceGeometry == false
                ? "Only whole source objects belonging to validated native dimensions were removed; other source geometry was preserved."
                : "Source PDFIMPORT primitives were preserved for review."));
    }

    private static void EraseSourceGeometry(
        Transaction transaction, ObjectId[] objectIds, IEnumerable<string> validatedProvenance)
    {
        var removable = new HashSet<string>(SourceReplacementPolicy.SelectWholeObjects(
            objectIds.Select(id => id.Handle.ToString()).ToArray(), validatedProvenance),
            StringComparer.OrdinalIgnoreCase);
        foreach (var objectId in objectIds)
        {
            if (!removable.Contains(objectId.Handle.ToString())) continue;
            if (transaction.GetObject(objectId, OpenMode.ForWrite, false) is Entity entity && !entity.IsErased)
                entity.Erase();
        }
    }

    private static void DumpFixture(Document document, ObjectId[] objectIds)
    {
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var scene = new AutoCadPrimitiveReader().Read(transaction, objectIds);
        var fixture = PrimitiveSceneFixtureFormatter.Format(
            scene,
            objectIds.Length,
            (int)database.Insunits);

        try
        {
            var outputDirectory = Path.Combine(Path.GetTempPath(), "TeyPdfCad");
            Directory.CreateDirectory(outputDirectory);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
            var outputPath = Path.Combine(outputDirectory, $"PrimitiveScene_{timestamp}.json");
            File.WriteAllText(outputPath, fixture, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            document.Editor.WriteMessage(
                $"\nTeyPdfCad fixture saved: {outputPath}\n" +
                $"selected={objectIds.Length}, lines={scene.Lines.Count}, texts={scene.Texts.Count}, " +
                $"INSUNITS={(int)database.Insunits}. Drawing was not changed.\n");
        }
        catch (System.Exception ex)
        {
            document.Editor.WriteMessage($"\nTeyPdfCad fixture export failed: {ex.Message}\n");
        }
    }

    private static ObjectId[] GetAllModelSpaceObjects(Database database)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var objectIds = modelSpace.Cast<ObjectId>().ToArray();
        transaction.Commit();
        return objectIds;
    }

    private static ObjectId[]? GetPdfImportSelection(Editor editor)
    {
        var selectionOptions = new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect objects produced by PDFIMPORT: "
        };
        var selection = editor.GetSelection(selectionOptions);
        if (selection.Status == PromptStatus.OK && selection.Value.Count > 0)
            return selection.Value.GetObjectIds();

        editor.WriteMessage("\nTeyPdfCad: nothing selected.");
        return null;
    }

    private static void WriteAnalysisReport(
        Editor editor,
        int selectedCount,
        int lineCount,
        int textCount,
        SemanticReconstructionResult semantic)
    {
        var scales = semantic.DetectedDrawingScales.Count == 0
            ? "none"
            : string.Join(", ", semantic.DetectedDrawingScales.Select(x => x.ToString("G8")));

        var summary = AnalysisReportFormatter.FormatSummary(
            selectedCount,
            lineCount,
            textCount,
            semantic.Dimensions.Count,
            semantic.DimensionChains.Count,
            scales,
            semantic.AverageDimensionConfidence);

        editor.WriteMessage($"\n{summary}\n");

        foreach (var dimension in semantic.Dimensions.Take(20))
        {
            editor.WriteMessage(
                $"  {dimension.Kind}: text='{dimension.SourceText}', value={dimension.DisplayedValue:G12}, " +
                $"scale={dimension.DrawingScale:G8}, confidence={dimension.Confidence:P1}, " +
                $"sources={dimension.ProvenanceIds.Count}\n");
        }

        if (semantic.Dimensions.Count > 20)
            editor.WriteMessage($"  ... {semantic.Dimensions.Count - 20} more dimensions.\n");
    }
}
