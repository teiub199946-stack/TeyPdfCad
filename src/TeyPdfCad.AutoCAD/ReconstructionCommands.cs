using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
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
            if (layout.ModelType) continue;
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

        if (semantic.Dimensions.Count == 0)
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
            settings?.Complete();
            editor.WriteMessage(
                $"\nTeyPdfCad: created sheet layout={sheetResult.LayoutName}, " +
                $"title block={sheetResult.BlockName}; no validated linear dimensions were found." +
                (sheetResult.PlotWarning is null ? string.Empty : $" Plot warning: {sheetResult.PlotWarning}"));
            return;
        }

        if (semantic.DetectedDrawingScales.Count != 1)
        {
            editor.WriteMessage(
                $"\nTeyPdfCad: {semantic.DetectedDrawingScales.Count} scale groups were detected " +
                $"[{string.Join(", ", semantic.DetectedDrawingScales.Select(x => x.ToString("G8")))}]. " +
                "Automatic global scaling is intentionally blocked until spatial scale partitioning is enabled. Drawing was not changed.");
            settings?.Fail("Multiple drawing scale groups were detected.");
            return;
        }

        var scale = semantic.DetectedDrawingScales[0];
        var normalizer = new SelectionScaleNormalizer();
        var transform = normalizer.BuildTransform(transaction, objectIds, scale);

        // Scaling the imported geometry is essential: the resulting native AutoCAD Dimension.Measurement
        // must equal the real drawing distance instead of merely displaying an overridden label.
        normalizer.Apply(transaction, objectIds, transform);

        var currentSpace = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
        var createdIds = new NativeDimensionWriter().Write(
            database,
            transaction,
            currentSpace,
            semantic.Dimensions,
            transform);

        var validation = new NativeDimensionValidator().Validate(
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
                EraseSourceGeometry(transaction, objectIds);
            settings.ApplyOutputUnits(database);
        }

        transaction.Commit();
        editor.Regen();

        settings?.Complete();

        editor.WriteMessage(
            $"\nTeyPdfCad: reconstructed {createdIds.Count} native dimensions. " +
            $"Scale={scale:G8}; max native measurement error={validation.MaxRelativeError:P4}. " +
            (sheetResult is null
                ? string.Empty
                : $" Sheet layout={sheetResult.LayoutName}, title block={sheetResult.BlockName}. " +
                  (sheetResult.PlotWarning is null ? string.Empty : $"Plot warning: {sheetResult.PlotWarning} ")) +
            (settings?.PreserveSourceGeometry == false
                ? "Source PDFIMPORT primitives were removed."
                : "Source PDFIMPORT primitives were preserved for review."));
    }

    private static void EraseSourceGeometry(Transaction transaction, ObjectId[] objectIds)
    {
        foreach (var objectId in objectIds)
        {
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
