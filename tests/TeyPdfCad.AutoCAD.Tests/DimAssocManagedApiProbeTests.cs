using Autodesk.AutoCAD.DatabaseServices;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class DimAssocManagedApiProbeTests
{
    [Fact]
    public void Managed_api_exposes_dimension_association_types()
    {
        var types = new[]
        {
            typeof(DimAssoc),
            typeof(OsnapPointRef),
            typeof(DimAssocPointType)
        };

        Assert.All(types, type => Assert.NotNull(type));
    }
}
