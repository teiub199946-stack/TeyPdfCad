namespace TeyPdfCad.AutoCAD;

public sealed record DwgAcceptanceSnapshot(
    int LayoutCount,
    int ModelSpaceEntityCount,
    int ViewportCount,
    int LayerCount,
    int DimensionCount,
    int LeaderCount);

public static class DwgAcceptanceAudit
{
    public static string Format(DwgAcceptanceSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"layoutCount\":{0},\"modelSpaceEntityCount\":{1},\"viewportCount\":{2},\"layerCount\":{3},\"dimensionCount\":{4},\"leaderCount\":{5}}}",
            snapshot.LayoutCount,
            snapshot.ModelSpaceEntityCount,
            snapshot.ViewportCount,
            snapshot.LayerCount,
            snapshot.DimensionCount,
            snapshot.LeaderCount);
    }
}
