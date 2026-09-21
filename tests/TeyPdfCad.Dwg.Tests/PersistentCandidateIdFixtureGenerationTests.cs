using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

/// <summary>
/// Produces a manual AutoCAD save/reopen fixture for Spike D.
/// It does not make any production suppression decision.
/// </summary>
public sealed class PersistentCandidateIdFixtureGenerationTests
{
    private const string AppName = "TEYCONVERT_CANDIDATE_V1";

    [Fact]
    public void Fixture_generator_emits_autocad_round_trip_probe()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "spike-d-fixtures");
        Directory.CreateDirectory(output);

        var drawingPath = Path.Combine(output, "persistent-candidate-id-probe.dwg");
        WriteProbe(drawingPath);
        File.WriteAllText(Path.Combine(output, "README.txt"), ManualInstructions);

        using var input = File.OpenRead(drawingPath);
        Assert.NotEmpty(DwgReader.Read(input).Entities);
    }

    private static void WriteProbe(string path)
    {
        var drawing = new CadDocument();

        var line = new Line(new XYZ(0, 0, 0), new XYZ(40, 0, 0));
        AddMetadata(line, "v1:1:LINE:001", "primary");
        drawing.Entities.Add(line);
        AddLabel(drawing, "LINE  v1:1:LINE:001  primary", new XYZ(0, -8, 0));

        var dimension = new DimensionAligned(new XYZ(0, 25, 0), new XYZ(40, 25, 0))
        {
            DefinitionPoint = new XYZ(0, 35, 0)
        };
        AddMetadata(dimension, "v1:1:DIMENSION:002", "primary");
        drawing.Entities.Add(dimension);
        AddLabel(drawing, "DIMENSION  v1:1:DIMENSION:002  primary", new XYZ(0, 42, 0));

        var leader = new Leader
        {
            ArrowHeadEnabled = true,
            CreationType = LeaderCreationType.CreatedWithTextAnnotation,
            PathType = LeaderPathType.StraightLineSegments,
            TextHeight = 3d
        };
        leader.Vertices.Add(new XYZ(70, 0, 0));
        leader.Vertices.Add(new XYZ(85, 15, 0));
        AddMetadata(leader, "v1:1:LEADER:003", "primary");
        drawing.Entities.Add(leader);

        var leaderText = new TextEntity
        {
            Value = "Leader annotation",
            InsertPoint = new XYZ(86, 16, 0),
            Height = 3d
        };
        AddMetadata(leaderText, "v1:1:LEADER:003", "annotation");
        drawing.Entities.Add(leaderText);
        AddLabel(drawing, "LEADER + TEXT  v1:1:LEADER:003", new XYZ(70, -8, 0));

        var boundary = new LwPolyline([new XY(110, 0), new XY(145, 0), new XY(145, 25), new XY(110, 25)])
        {
            IsClosed = true
        };
        drawing.Entities.Add(boundary);
        var hatch = new Hatch
        {
            IsSolid = true,
            Pattern = HatchPattern.Solid,
            SeedPoints = [new XY(127, 12)]
        };
        hatch.Paths.Add(new Hatch.BoundaryPath([boundary]));
        AddMetadata(hatch, "v1:1:HATCH:004", "primary");
        drawing.Entities.Add(hatch);
        AddLabel(drawing, "HATCH  v1:1:HATCH:004  primary", new XYZ(110, -8, 0));

        var block = new BlockRecord("SPIKE_LEVEL");
        block.Entities.Add(new AttributeDefinition
        {
            Tag = "LEVEL",
            Value = string.Empty,
            InsertPoint = new XYZ(0, 0, 0),
            Height = 3d
        });
        drawing.BlockRecords.Add(block);

        AddInsert(drawing, block, new XYZ(165, 0, 0), "+0.000", "v1:1:LEVEL:005");
        AddInsert(drawing, block, new XYZ(165, 25, 0), "+3.600", "v1:1:LEVEL:006");
        AddLabel(drawing, "Two INSERT instances of one BlockRecord; IDs must remain distinct.", new XYZ(155, -8, 0));

        using var output = File.Create(path);
        using var writer = new DwgWriter(output, drawing);
        writer.Write();
    }

    private static void AddInsert(CadDocument drawing, BlockRecord block, XYZ point, string value, string candidateId)
    {
        var insert = new Insert(block) { InsertPoint = point };
        insert.Attributes.Single().Value = value;
        AddMetadata(insert, candidateId, "primary");
        AddMetadata(insert.Attributes.Single(), candidateId, "attribute");
        drawing.Entities.Add(insert);
    }

    private static void AddMetadata(Entity entity, string candidateId, string role)
    {
        entity.ExtendedData.Add(AppName, new ExtendedData(
        [
            new ExtendedDataString(candidateId),
            new ExtendedDataString(role)
        ]));
    }

    private static void AddLabel(CadDocument drawing, string value, XYZ point)
    {
        drawing.Entities.Add(new TextEntity
        {
            Value = value,
            InsertPoint = point,
            Height = 2d,
            Style = TextStyle.Default
        });
    }

    private const string ManualInstructions = """
SPIKE D — ручная проверка Persistent CandidateId в AutoCAD 2022

Файл persistent-candidate-id-probe.dwg содержит XData под уникальным AppId:
TEYCONVERT_CANDIDATE_V1 → две плоские строки: CandidateId, Role.

Покрыты: Line, Dimension, Leader + Text, Hatch и два INSERT одного BlockRecord
с отдельными AttributeEntity. Подписи показывают ожидаемые значения, но сами
XData смотрите через AutoCAD XDATA / DXFOUT или после возвращённого save→reopen
в ACadSharp.

Порядок:
1. Откройте исходный DWG в AutoCAD 2022, выполните ZOOM EXTENTS.
2. Сохраните как persistent-candidate-id-probe_acad2022.dwg. Не перезаписывайте исходник.
3. Закройте и заново откройте сохранённую копию; убедитесь, что геометрия не повреждена.
4. Верните сохранённую копию. CI прочитает её ACadSharp и сравнит метаданные.

Критерии:
- у каждого entity ровно две строки под TEYCONVERT_CANDIDATE_V1;
- Leader и его annotation text имеют общий CandidateId, разные роли;
- два INSERT одного BlockRecord сохраняют разные CandidateId;
- metadata на INSERT / AttributeEntity, а не на BlockRecord.
""";
}
