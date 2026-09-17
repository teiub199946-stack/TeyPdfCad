namespace TeyPdfCad.Core.Compatibility;

internal static class NumericCompat
{
    public static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    public static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
