using TeyPdfCad.AutoCAD;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class DwgAcceptanceAuditTests
{
    [Fact]
    public void Format_emits_machine_readable_layout_and_semantic_counts()
    {
        var json = DwgAcceptanceAudit.Format(new DwgAcceptanceSnapshot(
            LayoutCount: 51,
            ModelSpaceEntityCount: 102,
            ViewportCount: 51,
            LayerCount: 8,
            DimensionCount: 3,
            LeaderCount: 2));

        Assert.Equal("{\"layoutCount\":51,\"modelSpaceEntityCount\":102,\"viewportCount\":51,\"layerCount\":8,\"dimensionCount\":3,\"leaderCount\":2}", json);
    }
}
