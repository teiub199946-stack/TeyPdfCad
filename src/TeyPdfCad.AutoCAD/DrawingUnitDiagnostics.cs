using Autodesk.AutoCAD.DatabaseServices;

namespace TeyPdfCad.AutoCAD;

internal static class DrawingUnitDiagnostics
{
    public static string Format(UnitsValue units)
    {
        var code = (int)units;

        if (units == UnitsValue.Millimeters)
        {
            return $"TeyPdfCad units: INSUNITS={code} ({units}). " +
                   "Engineering interpretation verified: 1 drawing unit = 1 mm.";
        }

        if (units == UnitsValue.Undefined)
        {
            return $"TeyPdfCad units: INSUNITS={code} ({units}). " +
                   "Engineering unit interpretation is unverified; drawing values are not automatically millimeters.";
        }

        return $"TeyPdfCad units: INSUNITS={code} ({units}). " +
               "Current drawing units are not millimeters; values must not be interpreted as mm without explicit conversion.";
    }
}
