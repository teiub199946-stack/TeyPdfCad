using System.Globalization;
using System.Text;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.AutoCAD;

internal static class PrimitiveSceneFixtureFormatter
{
    private const string Schema = "TeyPdfCad.PrimitiveScene.v1";

    public static string Format(PrimitiveScene scene, int selectedCount, int insunits)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (selectedCount < 0) throw new ArgumentOutOfRangeException(nameof(selectedCount));

        var lines = scene.Lines
            .OrderBy(x => x.Start.X)
            .ThenBy(x => x.Start.Y)
            .ThenBy(x => x.End.X)
            .ThenBy(x => x.End.Y)
            .ThenBy(x => x.Layer, StringComparer.Ordinal)
            .ThenBy(x => string.Join("\u001f", x.ProvenanceIds.OrderBy(id => id, StringComparer.Ordinal)), StringComparer.Ordinal)
            .ToArray();

        var texts = scene.Texts
            .OrderBy(x => x.Position.X)
            .ThenBy(x => x.Position.Y)
            .ThenBy(x => x.Value, StringComparer.Ordinal)
            .ThenBy(x => x.Height)
            .ThenBy(x => x.Rotation)
            .ThenBy(x => x.Layer, StringComparer.Ordinal)
            .ThenBy(x => string.Join("\u001f", x.ProvenanceIds.OrderBy(id => id, StringComparer.Ordinal)), StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();
        builder.Append('{');
        AppendPropertyName(builder, "schema");
        AppendJsonString(builder, Schema);
        builder.Append(',');
        AppendPropertyName(builder, "selectedCount");
        builder.Append(selectedCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendPropertyName(builder, "insunits");
        builder.Append(insunits.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendPropertyName(builder, "lines");
        builder.Append('[');

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) builder.Append(',');
            AppendLine(builder, lines[i]);
        }

        builder.Append(']');
        builder.Append(',');
        AppendPropertyName(builder, "texts");
        builder.Append('[');

        for (var i = 0; i < texts.Length; i++)
        {
            if (i > 0) builder.Append(',');
            AppendText(builder, texts[i]);
        }

        builder.Append(']');
        if (scene.Sheet is { } sheet)
        {
            builder.Append(',');
            AppendPropertyName(builder, "sheet");
            builder.Append('{');
            AppendPropertyName(builder, "widthMm");
            AppendDouble(builder, sheet.WidthMm);
            builder.Append(',');
            AppendPropertyName(builder, "heightMm");
            AppendDouble(builder, sheet.HeightMm);
            builder.Append(',');
            AppendPropertyName(builder, "format");
            AppendJsonString(builder, sheet.Format.ToString());
            builder.Append(',');
            AppendPropertyName(builder, "orientation");
            AppendJsonString(builder, sheet.Orientation.ToString());
            if (sheet.PageBounds is { } bounds)
            {
                builder.Append(',');
                AppendPropertyName(builder, "pageBounds");
                builder.Append('{');
                AppendPropertyName(builder, "minX");
                AppendDouble(builder, bounds.MinX);
                builder.Append(',');
                AppendPropertyName(builder, "minY");
                AppendDouble(builder, bounds.MinY);
                builder.Append(',');
                AppendPropertyName(builder, "widthMm");
                AppendDouble(builder, bounds.WidthMm);
                builder.Append(',');
                AppendPropertyName(builder, "heightMm");
                AppendDouble(builder, bounds.HeightMm);
                builder.Append(',');
                AppendPropertyName(builder, "drawingUnitsPerMm");
                AppendDouble(builder, bounds.DrawingUnitsPerMm);
                builder.Append(',');
                AppendPropertyName(builder, "units");
                AppendJsonString(builder, bounds.Units);
                builder.Append('}');
            }
            builder.Append('}');
        }
        if (scene.TitleBlock is { } titleBlock)
        {
            builder.Append(',');
            AppendPropertyName(builder, "titleBlock");
            builder.Append('{');
            AppendPropertyName(builder, "isCandidate");
            builder.Append(titleBlock.IsCandidate ? "true" : "false");
            builder.Append(',');
            AppendPropertyName(builder, "region");
            builder.Append('{');
            AppendPropertyName(builder, "minX");
            AppendDouble(builder, titleBlock.Region.MinX);
            builder.Append(',');
            AppendPropertyName(builder, "minY");
            AppendDouble(builder, titleBlock.Region.MinY);
            builder.Append(',');
            AppendPropertyName(builder, "maxX");
            AppendDouble(builder, titleBlock.Region.MaxX);
            builder.Append(',');
            AppendPropertyName(builder, "maxY");
            AppendDouble(builder, titleBlock.Region.MaxY);
            builder.Append(',');
            AppendPropertyName(builder, "sourceIds");
            AppendStringArray(builder, titleBlock.Region.ProvenanceIds);
            builder.Append('}');
            builder.Append(',');
            AppendPropertyName(builder, "fields");
            builder.Append('[');
            for (var i = 0; i < titleBlock.Fields.Count; i++)
            {
                if (i > 0) builder.Append(',');
                AppendTitleBlockField(builder, titleBlock.Fields[i]);
            }
            builder.Append(']');
            builder.Append(',');
            AppendPropertyName(builder, "lines");
            builder.Append('[');
            for (var i = 0; i < titleBlock.Lines.Count; i++)
            {
                if (i > 0) builder.Append(',');
                AppendLine(builder, titleBlock.Lines[i]);
            }
            builder.Append(']');
            builder.Append('}');
        }
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, LinePrimitive line)
    {
        builder.Append('{');
        AppendPropertyName(builder, "start");
        AppendPoint(builder, line.Start.X, line.Start.Y);
        builder.Append(',');
        AppendPropertyName(builder, "end");
        AppendPoint(builder, line.End.X, line.End.Y);
        builder.Append(',');
        AppendPropertyName(builder, "layer");
        AppendNullableJsonString(builder, line.Layer);
        builder.Append(',');
        AppendPropertyName(builder, "sourceIds");
        AppendStringArray(builder, line.ProvenanceIds);
        builder.Append('}');
    }

    private static void AppendText(StringBuilder builder, TextPrimitive text)
    {
        builder.Append('{');
        AppendPropertyName(builder, "value");
        AppendJsonString(builder, text.Value);
        builder.Append(',');
        AppendPropertyName(builder, "position");
        AppendPoint(builder, text.Position.X, text.Position.Y);
        builder.Append(',');
        AppendPropertyName(builder, "height");
        AppendDouble(builder, text.Height);
        builder.Append(',');
        AppendPropertyName(builder, "rotation");
        AppendDouble(builder, text.Rotation);
        builder.Append(',');
        AppendPropertyName(builder, "layer");
        AppendNullableJsonString(builder, text.Layer);
        builder.Append(',');
        AppendPropertyName(builder, "sourceIds");
        AppendStringArray(builder, text.ProvenanceIds);
        builder.Append('}');
    }

    private static void AppendTitleBlockField(StringBuilder builder, TitleBlockField field)
    {
        builder.Append('{');
        AppendPropertyName(builder, "kind");
        AppendJsonString(builder, field.Kind.ToString());
        builder.Append(',');
        AppendPropertyName(builder, "value");
        AppendJsonString(builder, field.Value);
        builder.Append(',');
        AppendPropertyName(builder, "position");
        AppendPoint(builder, field.Position.X, field.Position.Y);
        builder.Append(',');
        AppendPropertyName(builder, "height");
        AppendDouble(builder, field.Height);
        builder.Append(',');
        AppendPropertyName(builder, "rotation");
        AppendDouble(builder, field.Rotation);
        builder.Append(',');
        AppendPropertyName(builder, "sourceIds");
        AppendStringArray(builder, field.ProvenanceIds);
        builder.Append('}');
    }

    private static void AppendPoint(StringBuilder builder, double x, double y)
    {
        builder.Append('[');
        AppendDouble(builder, x);
        builder.Append(',');
        AppendDouble(builder, y);
        builder.Append(']');
    }

    private static void AppendDouble(StringBuilder builder, double value)
        => builder.Append(value.ToString("R", CultureInfo.InvariantCulture));

    private static void AppendStringArray(StringBuilder builder, IEnumerable<string> values)
    {
        builder.Append('[');
        var ordered = values.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            if (i > 0) builder.Append(',');
            AppendJsonString(builder, ordered[i]);
        }
        builder.Append(']');
    }

    private static void AppendPropertyName(StringBuilder builder, string name)
    {
        AppendJsonString(builder, name);
        builder.Append(':');
    }

    private static void AppendNullableJsonString(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        AppendJsonString(builder, value);
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        builder.Append('"');
    }
}
