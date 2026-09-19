using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations;

namespace TeyPdfCad.Pdf;

public sealed class PdfGraphicsOperationInterpreter
{
    public IReadOnlyList<VectorEntity> Interpret(IEnumerable<object> operations, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var entities = new List<VectorEntity>();
        Point2? currentPoint = null;
        var segments = new List<(Point2 Start, Point2 End)>();
        var strokeWidth = 1d;
        IReadOnlyList<double>? dashPattern = null;
        int? rgbColor = null;
        var sequence = 0;

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case SetLineWidth width:
                    strokeWidth = width.Width;
                    break;
                case SetLineDashPattern dash:
                    dashPattern = dash.Pattern.Array;
                    break;
                case SetStrokeColorDeviceRgb color:
                    rgbColor = ToRgb(color.R, color.G, color.B);
                    break;
                case BeginNewSubpath move:
                    currentPoint = ToMillimetres(move.X, move.Y);
                    break;
                case AppendStraightLineSegment line when currentPoint is Point2 start:
                    var end = ToMillimetres(line.X, line.Y);
                    segments.Add((start, end));
                    currentPoint = end;
                    break;
                case StrokePath:
                    foreach (var segment in segments)
                    {
                        sequence++;
                        entities.Add(new VectorLine(
                            $"page-{pageNumber}-line-{sequence}",
                            segment.Start,
                            segment.End,
                            new VectorStyle(RgbColor: rgbColor, StrokeWidthPoints: strokeWidth, DashPatternPoints: dashPattern)));
                    }
                    segments.Clear();
                    break;
            }
        }

        return entities;
    }

    private static int ToRgb(double red, double green, double blue)
    {
        var r = (int)Math.Round(Math.Clamp(red, 0d, 1d) * 255d);
        var g = (int)Math.Round(Math.Clamp(green, 0d, 1d) * 255d);
        var b = (int)Math.Round(Math.Clamp(blue, 0d, 1d) * 255d);
        return (r << 16) | (g << 8) | b;
    }

    private static Point2 ToMillimetres(double x, double y)
        => new(x * VectorPdfPage.MillimetresPerPoint, y * VectorPdfPage.MillimetresPerPoint);
}
