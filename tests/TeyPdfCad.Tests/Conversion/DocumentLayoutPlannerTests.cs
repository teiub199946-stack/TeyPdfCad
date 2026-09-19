using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using Xunit;

namespace TeyPdfCad.Tests.Conversion;

public sealed class DocumentLayoutPlannerTests
{
    [Fact]
    public void Fifty_one_pages_produce_fifty_one_stable_layout_names()
    {
        var pages = Enumerable.Range(1, 51)
            .Select(number => new VectorPdfPage(number, 595.276, 841.89, 0, []))
            .ToArray();

        var plan = new DocumentLayoutPlanner().Create(new VectorPdfDocument(pages));

        Assert.Equal(51, plan.Sheets.Count);
        Assert.Equal("Лист-001", plan.Sheets[0].LayoutName);
        Assert.Equal("Лист-051", plan.Sheets[50].LayoutName);
        Assert.Equal(0, plan.Sheets[0].ModelOriginX);
        Assert.True(plan.Sheets[1].ModelOriginX > plan.Sheets[0].ModelOriginX);
    }
}
