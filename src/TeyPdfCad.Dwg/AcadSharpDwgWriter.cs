using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using CSMath;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;

namespace TeyPdfCad.Dwg;

public sealed class AcadSharpDwgWriter
{
    public byte[] Write(VectorPdfDocument source, DwgDocumentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        var document = new CadDocument();
        var styles = new AcadSharpStyleCatalog(document);
        foreach (var sheet in plan.Sheets)
        {
            var layout = new Layout(sheet.LayoutName)
            {
                PaperWidth = sheet.PaperWidthMillimetres,
                PaperHeight = sheet.PaperHeightMillimetres
            };
            layout.AssociatedBlock.Entities.Add(new LwPolyline([
                new XY(0, 0),
                new XY(sheet.PaperWidthMillimetres, 0),
                new XY(sheet.PaperWidthMillimetres, sheet.PaperHeightMillimetres),
                new XY(0, sheet.PaperHeightMillimetres)
            ])
            {
                IsClosed = true
            });
            layout.AddViewport(new Viewport
            {
                Center = new XYZ(
                    sheet.PaperWidthMillimetres / 2d,
                    sheet.PaperHeightMillimetres / 2d,
                    0),
                Width = sheet.PaperWidthMillimetres,
                Height = sheet.PaperHeightMillimetres,
                ViewCenter = new XY(
                    sheet.ModelOriginX + sheet.PaperWidthMillimetres / 2d,
                    sheet.ModelOriginY + sheet.PaperHeightMillimetres / 2d),
                ViewHeight = sheet.PaperHeightMillimetres,
                ViewTarget = new XYZ(
                    sheet.ModelOriginX + sheet.PaperWidthMillimetres / 2d,
                    sheet.ModelOriginY + sheet.PaperHeightMillimetres / 2d,
                    0)
            });
            document.Layouts.Add(layout);
        }
        var sheetsByPage = plan.Sheets.ToDictionary(sheet => sheet.PageNumber);
        foreach (var page in source.Pages)
        {
            var sheet = sheetsByPage[page.Number];
            foreach (var sourceLine in page.Entities.OfType<VectorLine>())
            {
                var line = new Line(
                    new XYZ(sheet.ModelOriginX + sourceLine.Start.X, sheet.ModelOriginY + sourceLine.Start.Y, 0),
                    new XYZ(sheet.ModelOriginX + sourceLine.End.X, sheet.ModelOriginY + sourceLine.End.Y, 0));
                styles.Apply(line, sourceLine.Style);
                document.Entities.Add(line);
            }
            foreach (var sourcePolyline in page.Entities.OfType<VectorPolyline>())
            {
                var polyline = new LwPolyline(sourcePolyline.Vertices.Select(vertex => new XY(
                    sheet.ModelOriginX + vertex.X,
                    sheet.ModelOriginY + vertex.Y)))
                {
                    IsClosed = sourcePolyline.IsClosed
                };
                styles.Apply(polyline, sourcePolyline.Style);
                document.Entities.Add(polyline);
            }
            foreach (var sourceText in page.Entities.OfType<VectorText>())
            {
                var text = new TextEntity
                {
                    Value = sourceText.Value,
                    InsertPoint = new XYZ(
                        sheet.ModelOriginX + sourceText.InsertionPoint.X,
                        sheet.ModelOriginY + sourceText.InsertionPoint.Y,
                        0),
                    Height = sourceText.HeightPoints * VectorPdfPage.MillimetresPerPoint
                };
                styles.Apply(text, sourceText.Style);
                document.Entities.Add(text);
            }
        }
        using var output = new MemoryStream();
        using var writer = new DwgWriter(output, document);
        writer.Write();
        return output.ToArray();
    }
}
