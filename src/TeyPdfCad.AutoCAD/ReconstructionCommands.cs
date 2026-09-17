using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.AutoCAD;

public sealed class ReconstructionCommands
{
    [CommandMethod("TEYPDFRECONSTRUCT", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ReconstructSelectedPdfImportObjects()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;

        var editor = document.Editor;
        var database = document.Database;

        var selectionOptions = new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect objects produced by PDFIMPORT: "
        };
        var selection = editor.GetSelection(selectionOptions);
        if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
        {
            editor.WriteMessage("\nTeyPdfCad: nothing selected.");
            return;
        }

        var objectIds = selection.Value.GetObjectIds();

        using var transaction = database.TransactionManager.StartTransaction();

        var scene = new AutoCadPrimitiveReader().Read(transaction, objectIds);
        var semantic = new SemanticReconstructionEngine().Analyze(scene);

        if (semantic.Dimensions.Count == 0)
        {
            editor.WriteMessage("\nTeyPdfCad: no validated linear dimensions were reconstructed. Drawing was not changed.");
            return;
        }

        if (semantic.DetectedDrawingScales.Count != 1)
        {
            editor.WriteMessage(
                $"\nTeyPdfCad: {semantic.DetectedDrawingScales.Count} scale groups were detected. " +
                "Automatic global scaling is intentionally blocked until spatial scale partitioning is enabled. Drawing was not changed.");
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
            return;
        }

        transaction.Commit();
        editor.Regen();

        editor.WriteMessage(
            $"\nTeyPdfCad: reconstructed {createdIds.Count} native dimensions. " +
            $"Scale={scale:G8}; max native measurement error={validation.MaxRelativeError:P4}. " +
            "Source PDFIMPORT primitives were preserved for review.");
    }
}
