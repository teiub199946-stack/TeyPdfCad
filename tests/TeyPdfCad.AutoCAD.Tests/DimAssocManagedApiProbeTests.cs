using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class DimAssocManagedApiProbeTests
{
    [Fact]
    public void AutoCAD_2022_managed_api_does_not_export_native_dimassoc_types()
    {
        var exportedNames = typeof(Dimension).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToArray();

        Assert.DoesNotContain("DimAssoc", exportedNames);
        Assert.DoesNotContain("OsnapPointRef", exportedNames);
        Assert.DoesNotContain("DimAssocPointType", exportedNames);
    }

    [Fact]
    public void Database_exposes_DIMASSOC_system_setting_but_dimension_has_no_public_assoc_member()
    {
        var dimAssocProperty = typeof(Database).GetProperty(
            "DimAssoc",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(dimAssocProperty);

        var publicDimensionAssocMembers = typeof(Dimension)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member.Name.IndexOf("Assoc", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(publicDimensionAssocMembers);
    }
}
