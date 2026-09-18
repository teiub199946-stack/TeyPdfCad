using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;

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

        var objectIds = GetPdfImportSelection(document.Editor);
        if (objectIds is null) return;

        DumpFixture(document, objectIds);
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

        var objectIds = GetPdfImportSelection(document.Editor);
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

        if (semantic.Dimensions.Count == 0)
        {
            editor.WriteMessage("\nTeyPdfCad: no validated linear dimensions were reconstructed. Drawing was not changed.");
            settings?.Fail("No validated linear dimensions were reconstructed.");
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
            (settings?.PreserveSourceGeometry == false
                ? "Source PDFIMPORT primitives were removed."
                : "Source PDFIMPORT primitives were preserved for review."));
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

        // No commit: fixture capture is intentionally read-only.
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

    private static void EraseSourceGeometry(Transaction transaction, ObjectId[] objectIds)
    {
        foreach (var objectId in objectIds)
        {
            if (transaction.GetObject(objectId, OpenMode.ForWrite, false) is Entity entity && !entity.IsErased)
                entity.Erase();
        }
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
