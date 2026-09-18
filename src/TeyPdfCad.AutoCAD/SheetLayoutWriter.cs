using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.AutoCAD;

internal sealed class SheetLayoutWriter
{
    private const string LayoutPrefix = "TeyPdfCad_A3";
    private const string BlockPrefix = "TeyPdfCad_TitleBlock";

    public SheetWriteResult Write(
        Database database,
        Transaction transaction,
        SheetMetadata sheet,
        TitleBlockMetadata titleBlock)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        if (sheet is null) throw new ArgumentNullException(nameof(sheet));
        if (titleBlock is null) throw new ArgumentNullException(nameof(titleBlock));
        if (!titleBlock.IsCandidate) throw new ArgumentException("Title block must be an accepted candidate.", nameof(titleBlock));

        var bounds = sheet.PageBounds ?? new SheetPageBounds(0, 0, sheet.WidthMm, sheet.HeightMm);
        var layoutName = $"{LayoutPrefix}_{sheet.Orientation}";
        var layoutId = GetOrCreateLayout(database, transaction, layoutName);
        var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForWrite);
        var plotWarning = ConfigureA3PlotSettings(layout, sheet);
        var paperSpace = (BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);

        // Paper Space is expressed in physical millimetres. The imported PDF geometry stays in
        // Model Space and is shown through a viewport using the measured drawing scale.
        try
        {
            ClearGeneratedPaperSpace(transaction, paperSpace);
            WriteViewport(transaction, paperSpace, bounds, sheet);
        }
        catch (System.Exception ex)
        {
            throw new InvalidOperationException($"viewport/frame stage failed: {ex.Message}", ex);
        }
        var paperBounds = new SheetPageBounds(0, 0, sheet.WidthMm, sheet.HeightMm);
        var frameIds = WriteFrame(transaction, paperSpace, paperBounds);
        var blockName = $"{BlockPrefix}_{sheet.Orientation}";
        var drawingUnitsPerMm = bounds.DrawingUnitsPerMm;
        ObjectId blockId;
        try
        {
            blockId = WriteTitleBlockDefinition(database, transaction, blockName, titleBlock, drawingUnitsPerMm);
        }
        catch (System.Exception ex)
        {
            throw new InvalidOperationException($"title block definition stage failed: {ex.Message}", ex);
        }
        var insertion = new Point3d(
            (titleBlock.Region.MinX - bounds.MinX) / drawingUnitsPerMm,
            (titleBlock.Region.MinY - bounds.MinY) / drawingUnitsPerMm,
            0);
        var reference = new BlockReference(insertion, blockId);
        var referenceId = paperSpace.AppendEntity(reference);
        transaction.AddNewlyCreatedDBObject(reference, true);

        return new SheetWriteResult(layoutId, blockId, referenceId, frameIds, layoutName, blockName, plotWarning);
    }

    private static ObjectId GetOrCreateLayout(
        Database database,
        Transaction transaction,
        string layoutName)
    {
        // LayoutManager.CreateLayout/DeleteLayout open their own transaction inside the caller's
        // active transaction (see ReconstructionCommands.Reconstruct). That violates AutoCAD 2022
        // database invariants and surfaces as eNotInDatabase when the returned ids are opened in
        // the surrounding transaction. Create and refresh the layout transactionally instead,
        // using the layout dictionary and block table directly.
        var layoutDict = (DBDictionary)transaction.GetObject(
            database.LayoutDictionaryId,
            OpenMode.ForWrite);
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForWrite);

        BlockTableRecord paperSpace;
        string blockName = $"*Paper_Space_{layoutName}";
        if (blockTable.Has(blockName))
        {
            paperSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[blockName],
                OpenMode.ForWrite);
        }
        else
        {
            paperSpace = new BlockTableRecord
            {
                Name = blockName,
            };
            blockTable.Add(paperSpace);
            transaction.AddNewlyCreatedDBObject(paperSpace, true);
        }

        Layout layout;
        if (layoutDict.Contains(layoutName))
        {
            var existingId = layoutDict.GetAt(layoutName);
            layout = (Layout)transaction.GetObject(existingId, OpenMode.ForWrite);
            layout.BlockTableRecordId = paperSpace.ObjectId;
        }
        else
        {
            layout = new Layout
            {
                LayoutName = layoutName,
            };
            var layoutId = layoutDict.SetAt(layoutName, layout);
            transaction.AddNewlyCreatedDBObject(layout, true);
            layout.BlockTableRecordId = paperSpace.ObjectId;
        }

        // A layout needs at least one resident paper-space viewport before its block can be
        // used by downstream viewport/frame stages. Give it the default one transactionally so
        // no LayoutManager-style object ever points at a non-database record.
        EnsureDefaultViewport(transaction, paperSpace);
        return layout.ObjectId;
    }

    private static void EnsureDefaultViewport(
        Transaction transaction,
        BlockTableRecord paperSpace)
    {
        foreach (var id in paperSpace.Cast<ObjectId>().ToArray())
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is Viewport)
                return;
        }

        var defaultViewport = new Viewport();
        paperSpace.AppendEntity(defaultViewport);
        transaction.AddNewlyCreatedDBObject(defaultViewport, true);
        defaultViewport.SetDatabaseDefaults();
        defaultViewport.CenterPoint = Point3d.Origin;
        defaultViewport.Width = 1;
        defaultViewport.Height = 1;
        defaultViewport.On = false;
    }

    private static void ClearGeneratedPaperSpace(Transaction transaction, BlockTableRecord paperSpace)
    {
        foreach (var objectId in paperSpace.Cast<ObjectId>().ToArray())
        {
            Entity? entity;
            try
            {
                entity = transaction.GetObject(objectId, OpenMode.ForRead, false) as Entity;
            }
            catch (System.Exception)
            {
                continue;
            }

            if (entity is null || entity.IsErased)
                continue;

            // Keep AutoCAD's required paper-space viewport. Erasing it leaves the Layout
            // pointing at a non-database object and causes eNotInDatabase on the next write.
            if (entity is Viewport)
                continue;

            if (entity is Line or BlockReference or DBText or MText)
            {
                entity.UpgradeOpen();
                entity.Erase();
            }
        }
    }

    private static ObjectId WriteViewport(
        Transaction transaction,
        BlockTableRecord paperSpace,
        SheetPageBounds drawingBounds,
        SheetMetadata sheet)
    {
        const double marginMm = 10;
        var paperWidth = sheet.WidthMm;
        var paperHeight = sheet.HeightMm;
        // AutoCAD creates a special default viewport with every new layout. It is not safe to
        // modify through the managed API on all 2022 profiles, so leave it untouched and add
        // one owned viewport for the reconstructed sheet.
        var viewport = new Viewport();
        var id = paperSpace.AppendEntity(viewport);
        transaction.AddNewlyCreatedDBObject(viewport, true);

        // SetDatabaseDefaults() must run only after the viewport is resident in the database.
        // Calling it on a not-yet-added viewport keeps the managed object detached and the next
        // ViewportTableRecord access raises eNotInDatabase in AutoCAD 2022.
        viewport.SetDatabaseDefaults();
        viewport.CenterPoint = new Point3d(paperWidth / 2, paperHeight / 2, 0);
        viewport.Width = Math.Max(1, paperWidth - (marginMm * 2));
        viewport.Height = Math.Max(1, paperHeight - (marginMm * 2));
        viewport.ViewCenter = new Point2d(
            drawingBounds.MinX + (drawingBounds.DrawingWidth / 2),
            drawingBounds.MinY + (drawingBounds.DrawingHeight / 2));
        viewport.ViewHeight = Math.Max(1, (paperHeight - (marginMm * 2)) * drawingBounds.DrawingUnitsPerMm);
        viewport.On = true;
        return id;
    }

    private static IReadOnlyList<ObjectId> WriteFrame(
        Transaction transaction,
        BlockTableRecord paperSpace,
        SheetPageBounds bounds)
    {
        var min = new Point3d(bounds.MinX, bounds.MinY, 0);
        var max = new Point3d(bounds.MaxX, bounds.MaxY, 0);
        var corners = new[]
        {
            min,
            new Point3d(max.X, min.Y, 0),
            max,
            new Point3d(min.X, max.Y, 0),
        };
        var ids = new List<ObjectId>(4);
        for (var i = 0; i < corners.Length; i++)
        {
            var line = new Line(corners[i], corners[(i + 1) % corners.Length]);
            line.Layer = "0";
            var id = paperSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
            ids.Add(id);
        }

        return ids;
    }

    private static string? ConfigureA3PlotSettings(Layout layout, SheetMetadata sheet)
    {
        if (sheet.Format != StandardSheetFormat.A3)
            throw new InvalidOperationException("Sheet layout writer currently accepts only detected A3 sheets.");

        using var settings = new PlotSettings(layout.ModelType);
        settings.CopyFrom(layout);
        var validator = PlotSettingsValidator.Current;

        var mediaNames = sheet.Orientation == SheetOrientation.Portrait
            ? new[]
            {
                "ISO_A3_(297.00_x_420.00_MM)",
                "ISO_full_bleed_A3_(297.00_x_420.00_MM)",
            }
            : new[]
            {
                "ISO_A3_(420.00_x_297.00_MM)",
                "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
            };

        string? warning = null;
        var configured = false;
        foreach (var mediaName in mediaNames)
        {
            try
            {
                validator.SetPlotConfigurationName(settings, "DWG To PDF.pc3", mediaName);
                validator.RefreshLists(settings);
                configured = true;
                break;
            }
            catch (System.Exception ex)
            {
                warning = $"A3 plot media '{mediaName}' was not accepted: {ex.Message}";
            }
        }

        try
        {
            validator.SetPlotType(settings, PlotType.Layout);
            validator.SetUseStandardScale(settings, true);
            validator.SetStdScaleType(settings, StdScaleType.ScaleToFit);
            validator.SetPlotCentered(settings, true);
        }
        catch (System.Exception ex)
        {
            warning = $"Basic plot settings were not accepted: {ex.Message}";
        }

        layout.CopyFrom(settings);

        if (!configured && warning is null)
            warning = "No supported DWG To PDF A3 media was found; the editable sheet layout was kept with the current plot device.";

        return warning;
    }

    private static ObjectId WriteTitleBlockDefinition(
        Database database,
        Transaction transaction,
        string blockName,
        TitleBlockMetadata titleBlock,
        double drawingUnitsPerMm)
    {
        if (double.IsNaN(drawingUnitsPerMm) || double.IsInfinity(drawingUnitsPerMm) || drawingUnitsPerMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(drawingUnitsPerMm));

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        BlockTableRecord definition;
        ObjectId blockId;
        if (blockTable.Has(blockName))
        {
            blockId = blockTable[blockName];
            definition = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForWrite);
            foreach (var objectId in definition.Cast<ObjectId>().ToArray())
            {
                if (transaction.GetObject(objectId, OpenMode.ForWrite, false) is Entity entity && !entity.IsErased)
                    entity.Erase();
            }
        }
        else
        {
            blockTable.UpgradeOpen();
            definition = new BlockTableRecord { Name = blockName };
            blockId = blockTable.Add(definition);
            transaction.AddNewlyCreatedDBObject(definition, true);
        }

        foreach (var sourceLine in titleBlock.Lines)
        {
            var line = new Line(
                ToLocal(sourceLine.Start, titleBlock.Region, drawingUnitsPerMm),
                ToLocal(sourceLine.End, titleBlock.Region, drawingUnitsPerMm));
            line.Layer = "0";
            ApplyLineWeight(line, sourceLine.StrokeWidthMm);
            definition.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
        }

        foreach (var field in titleBlock.Fields)
        {
            var text = new DBText
            {
                TextString = field.Value,
                Position = ToLocal(field.Position, titleBlock.Region, drawingUnitsPerMm),
                Height = field.Height / drawingUnitsPerMm,
                Rotation = field.Rotation,
                Layer = "0",
            };
            definition.AppendEntity(text);
            transaction.AddNewlyCreatedDBObject(text, true);
        }

        return blockId;
    }

    private static Point3d ToLocal(
        TeyPdfCad.Core.Geometry.Point2 point,
        TitleBlockRegion region,
        double drawingUnitsPerMm)
        => new(
            (point.X - region.MinX) / drawingUnitsPerMm,
            (point.Y - region.MinY) / drawingUnitsPerMm,
            0);

    private static void ApplyLineWeight(Entity entity, double? strokeWidthMm)
    {
        if (strokeWidthMm is not { } width || double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            return;

        var hundredths = Math.Max(1, Math.Min(211, (int)Math.Round(width * 100, MidpointRounding.AwayFromZero)));
        entity.LineWeight = (LineWeight)hundredths;
    }
}

internal sealed record SheetWriteResult(
    ObjectId LayoutId,
    ObjectId BlockId,
    ObjectId BlockReferenceId,
    IReadOnlyList<ObjectId> FrameIds,
    string LayoutName,
    string BlockName,
    string? PlotWarning);
