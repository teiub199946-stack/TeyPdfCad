using TeyPdfCad.Core.Sheets;
using TeyPdfCad.Core.Templates;
using Xunit;

namespace TeyPdfCad.Tests.Templates;

public sealed class TemplateSheetSelectorTests
{
    [Fact]
    public void Confirms_matching_standard_template_only_with_title_block_candidate()
    {
        var library = new TemplateLibrary([new TemplateSheet("A3-landscape", StandardSheetFormat.A3, SheetOrientation.Landscape)]);
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape);
        var titleBlock = new TitleBlockMetadata(new TitleBlockRegion(290, 0, 420, 70, ["stamp-line", "stamp-text"]), [], true);

        var result = new TemplateSheetSelector(library).Select(sheet, titleBlock);

        Assert.True(result.IsConfirmed);
        Assert.Equal("A3-landscape", result.TemplateName);
        Assert.Equal(["stamp-line", "stamp-text"], result.SourceIdsToReplace);
    }

    [Fact]
    public void Rejects_standard_size_without_confirmed_title_block()
    {
        var library = new TemplateLibrary([new TemplateSheet("A3-landscape", StandardSheetFormat.A3, SheetOrientation.Landscape)]);
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape);

        var result = new TemplateSheetSelector(library).Select(sheet, null);

        Assert.False(result.IsConfirmed);
        Assert.Null(result.TemplateName);
    }
}
