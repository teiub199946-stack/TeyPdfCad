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
        foreach (var sheet in plan.Sheets)
        {
            var layout = new Layout(sheet.LayoutName)
            {
                PaperWidth = sheet.PaperWidthMillimetres,
                PaperHeight = sheet.PaperHeightMillimetres
            };
            document.Layouts.Add(layout);
        }
        foreach (var sourceLine in source.Pages.SelectMany(page => page.Entities).OfType<VectorLine>())
        {
            document.Entities.Add(new Line(
                new XYZ(sourceLine.Start.X, sourceLine.Start.Y, 0),
                new XYZ(sourceLine.End.X, sourceLine.End.Y, 0)));
        }
        foreach (var sourcePolyline in source.Pages.SelectMany(page => page.Entities).OfType<VectorPolyline>())
        {
            var polyline = new LwPolyline(sourcePolyline.Vertices.Select(vertex => new XY(vertex.X, vertex.Y)))
            {
                IsClosed = sourcePolyline.IsClosed
            };
            document.Entities.Add(polyline);
        }
        using var output = new MemoryStream();
        using var writer = new DwgWriter(output, document);
        writer.Write();
        return output.ToArray();
    }
}
