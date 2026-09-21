using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Sheets;

public static class TitleBlockDetector
{
    public static TitleBlockMetadata? DetectGeometryOnly(
        PrimitiveScene scene,
        SheetMetadata sheet,
        double widthFraction = 0.32,
        double heightFraction = 0.24)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (sheet is null) throw new ArgumentNullException(nameof(sheet));

        var region = BuildRegion(scene, sheet, widthFraction, heightFraction);
        if (region is null) return null;

        var lines = scene.Lines.Where(line => Intersects(line, region)).ToArray();
        if (lines.Length < 2) return null;

        var sourceIds = lines
            .SelectMany(line => line.ProvenanceIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new TitleBlockMetadata(region with { SourceIds = sourceIds }, [], IsCandidate: true)
        {
            Lines = lines,
        };
    }

    public static TitleBlockMetadata? Detect(
        PrimitiveScene scene,
        SheetMetadata sheet,
        double widthFraction = 0.32,
        double heightFraction = 0.24)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (sheet is null) throw new ArgumentNullException(nameof(sheet));
        if (!IsFinite(widthFraction) || widthFraction <= 0 || widthFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(widthFraction));
        if (!IsFinite(heightFraction) || heightFraction <= 0 || heightFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(heightFraction));

        var region = BuildRegion(scene, sheet, widthFraction, heightFraction);
        if (region is null) return null;

        var fields = scene.Texts
            .Where(text => region.Contains(text.Position))
            .Select(text => new TitleBlockField(
                TitleBlockFieldKind.Unknown,
                text.Value,
                text.Position,
                text.Height,
                text.Rotation,
                text.ProvenanceIds))
            .OrderBy(field => field.Position.Y)
            .ThenBy(field => field.Position.X)
            .ToArray();

        var sourceIds = scene.Lines
            .Where(line => Intersects(line, region))
            .SelectMany(line => line.ProvenanceIds)
            .Concat(fields.SelectMany(field => field.ProvenanceIds))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        // A candidate needs both a text signal and at least two source segments.
        // Unknown fields remain editable text; this detector never invents semantics.
        var lineCount = scene.Lines.Count(line => Intersects(line, region));
        if (fields.Length == 0 || lineCount < 2) return null;

        var selectedLines = scene.Lines
            .Where(line => Intersects(line, region))
            .ToArray();

        return new TitleBlockMetadata(
            region with { SourceIds = sourceIds },
            fields,
            IsCandidate: true)
        {
            Lines = selectedLines,
        };
    }

    private static TitleBlockRegion? BuildRegion(
        PrimitiveScene scene,
        SheetMetadata sheet,
        double widthFraction,
        double heightFraction)
    {
        var geometry = scene.Lines.SelectMany(line => new[] { line.Start, line.End });
        var textPoints = scene.Texts.Select(text => text.Position);
        var points = geometry.Concat(textPoints).ToArray();
        if (points.Length == 0) return null;

        var minY = sheet.PageBounds?.MinY ?? points.Min(point => point.Y);
        var maxX = sheet.PageBounds?.MaxX ?? points.Max(point => point.X);
        return new TitleBlockRegion(
            maxX - (sheet.PageBounds?.DrawingWidth ?? sheet.WidthMm) * widthFraction,
            minY,
            maxX,
            minY + (sheet.PageBounds?.DrawingHeight ?? sheet.HeightMm) * heightFraction);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool Intersects(LinePrimitive line, TitleBlockRegion region)
    {
        var dx = line.End.X - line.Start.X;
        var dy = line.End.Y - line.Start.Y;
        var minimum = 0.0;
        var maximum = 1.0;

        return Clip(-dx, line.Start.X - region.MinX, ref minimum, ref maximum) &&
               Clip(dx, region.MaxX - line.Start.X, ref minimum, ref maximum) &&
               Clip(-dy, line.Start.Y - region.MinY, ref minimum, ref maximum) &&
               Clip(dy, region.MaxY - line.Start.Y, ref minimum, ref maximum);
    }

    private static bool Clip(double coefficient, double bound, ref double minimum, ref double maximum)
    {
        const double Epsilon = 1e-12;
        if (Math.Abs(coefficient) <= Epsilon)
            return bound >= -Epsilon;

        var parameter = bound / coefficient;
        if (coefficient < 0)
        {
            if (parameter > maximum) return false;
            if (parameter > minimum) minimum = parameter;
        }
        else
        {
            if (parameter < minimum) return false;
            if (parameter < maximum) maximum = parameter;
        }

        return minimum <= maximum + Epsilon;
    }
}
