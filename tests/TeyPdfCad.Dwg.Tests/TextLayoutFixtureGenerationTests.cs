using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

/// <summary>
/// Produces manual AutoCAD evidence for Spike B. The files intentionally remain
/// outside source control and are uploaded by CI as a review artifact.
/// </summary>
public sealed class TextLayoutFixtureGenerationTests
{
    [Fact]
    public void Fixture_generator_emits_manual_autocad_probe_drawings()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "spike-b-fixtures");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"), ManualInstructions);

        foreach (var font in new[]
                 {
                     new FontFixture("arial", "arial.ttf"),
                     new FontFixture("simplex", "simplex.shx")
                 })
        {
            foreach (var angle in new[] { 0d, Math.PI / 4d, Math.PI / 2d })
            {
                var name = $"fixture-{ToDegrees(angle)}-{font.Id}.dwg";
                WriteComparisonFixture(Path.Combine(output, name), font, angle);
            }
        }

        WriteRotationConflictProbe(Path.Combine(output, "fixture-fit-rotation-conflict.dwg"));

        var fixtures = Directory.GetFiles(output, "*.dwg");
        Assert.Equal(7, fixtures.Length);
        foreach (var path in fixtures)
        {
            using var input = File.OpenRead(path);
            Assert.NotEmpty(DwgReader.Read(input).Entities);
        }
    }

    private static void WriteComparisonFixture(string path, FontFixture font, double rotation)
    {
        var drawing = new CadDocument();
        var style = new TextStyle("SPIKE_" + font.Id.ToUpperInvariant())
        {
            Filename = font.Filename
        };
        drawing.TextStyles.Add(style);

        AddComparisonCell(drawing, style, "PLAIN LEFT", "ABCDE", new XYZ(20, 190, 0), rotation, TextLayoutMode.PlainLeft);
        AddComparisonCell(drawing, style, "WIDTH FACTOR 0.80", "ABCDE", new XYZ(120, 190, 0), rotation, TextLayoutMode.WidthFactor);
        AddComparisonCell(drawing, style, "FIT", "ABCDE", new XYZ(220, 190, 0), rotation, TextLayoutMode.Fit);

        AddComparisonCell(drawing, style, "PLAIN LEFT", "ТЕСТ-45", new XYZ(20, 90, 0), rotation, TextLayoutMode.PlainLeft);
        AddComparisonCell(drawing, style, "WIDTH FACTOR 0.80", "ТЕСТ-45", new XYZ(120, 90, 0), rotation, TextLayoutMode.WidthFactor);
        AddComparisonCell(drawing, style, "FIT", "ТЕСТ-45", new XYZ(220, 90, 0), rotation, TextLayoutMode.Fit);

        using var output = File.Create(path);
        using var writer = new DwgWriter(output, drawing);
        writer.Write();
    }

    private static void AddComparisonCell(
        CadDocument drawing,
        TextStyle style,
        string modeLabel,
        string value,
        XYZ start,
        double rotation,
        TextLayoutMode mode)
    {
        const double baselineLength = 52d;
        var end = new XYZ(
            start.X + baselineLength * Math.Cos(rotation),
            start.Y + baselineLength * Math.Sin(rotation),
            0d);

        // Visible baseline and end ticks define the exact target interval.
        drawing.Entities.Add(new Line(start, end));
        AddTick(drawing, start, rotation);
        AddTick(drawing, end, rotation);

        var text = new TextEntity
        {
            Value = value,
            InsertPoint = start,
            Height = 10d,
            Rotation = rotation,
            Style = style,
            HorizontalAlignment = mode == TextLayoutMode.Fit
                ? TextHorizontalAlignment.Fit
                : TextHorizontalAlignment.Left,
            VerticalAlignment = TextVerticalAlignmentType.Baseline,
            WidthFactor = mode == TextLayoutMode.WidthFactor ? 0.8d : 1d
        };
        if (mode == TextLayoutMode.Fit)
        {
            text.AlignmentPoint = end;
        }
        drawing.Entities.Add(text);

        drawing.Entities.Add(new TextEntity
        {
            Value = modeLabel,
            InsertPoint = new XYZ(start.X, start.Y - 18d, 0d),
            Height = 3d,
            Style = TextStyle.Default
        });
    }

    private static void AddTick(CadDocument drawing, XYZ point, double rotation)
    {
        const double halfLength = 3d;
        var dx = Math.Cos(rotation + Math.PI / 2d) * halfLength;
        var dy = Math.Sin(rotation + Math.PI / 2d) * halfLength;
        drawing.Entities.Add(new Line(
            new XYZ(point.X - dx, point.Y - dy, 0d),
            new XYZ(point.X + dx, point.Y + dy, 0d)));
    }

    private static void WriteRotationConflictProbe(string path)
    {
        var drawing = new CadDocument();
        var start = new XYZ(20d, 80d, 0d);
        var end = new XYZ(100d, 80d, 0d);
        drawing.Entities.Add(new Line(start, end));
        AddTick(drawing, start, 0d);
        AddTick(drawing, end, 0d);
        drawing.Entities.Add(new TextEntity
        {
            Value = "FIT ROTATION PROBE",
            InsertPoint = start,
            AlignmentPoint = end,
            Height = 10d,
            Rotation = Math.PI / 2d,
            HorizontalAlignment = TextHorizontalAlignment.Fit,
            VerticalAlignment = TextVerticalAlignmentType.Baseline
        });
        drawing.Entities.Add(new TextEntity
        {
            Value = "Expected baseline is horizontal; stored Rotation is deliberately vertical.",
            InsertPoint = new XYZ(20d, 50d, 0d),
            Height = 3d,
            Style = TextStyle.Default
        });

        using var output = File.Create(path);
        using var writer = new DwgWriter(output, drawing);
        writer.Write();
    }

    private static int ToDegrees(double radians)
        => (int)Math.Round(radians * 180d / Math.PI);

    private sealed record FontFixture(string Id, string Filename);

    private const string ManualInstructions = """
SPIKE B — ручная проверка AutoCAD 2022

Файлы fixture-{0,45,90}-{arial,simplex}.dwg содержат шесть изолированных ячеек:
ABCDE и ТЕСТ-45 × PLAIN LEFT, WIDTH FACTOR 0.80, FIT.
Тонкий отрезок с поперечными рисками — контрольная baseline длиной 52 drawing units.

Для каждого файла:
1. Откройте его в AutoCAD 2022 и выполните ZOOM EXTENTS.
2. Для FIT проверьте: строка визуально начинается и заканчивается на рисках baseline,
   остаётся читаемой и не меняет видимую высоту.
3. Для PLAIN/WIDTH FACTOR сравните длину и читаемость с FIT.
4. В simplex-файлах отдельно отметьте, отображается ли кириллица без подстановки символов.
5. Сохраните копию как *_acad2022.dwg (оригиналы не перезаписывать).

fixture-fit-rotation-conflict.dwg — диагностический случай:
baseline горизонтальна, но Rotation намеренно равен 90°. Запишите, какой ориентацией
AutoCAD реально показал FIT-текст; сохраните копию *_acad2022.dwg.

Верните сохранённые копии и/или скриншоты с ZOOM EXTENTS. Это позволит выбрать
FIT/WidthFactor policy, не подменяя визуальную проверку структурным тестом.
""";

    private enum TextLayoutMode
    {
        PlainLeft,
        WidthFactor,
        Fit
    }
}
