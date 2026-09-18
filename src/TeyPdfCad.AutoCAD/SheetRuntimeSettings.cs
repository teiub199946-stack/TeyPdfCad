using TeyPdfCad.Core.Sheets;

namespace TeyPdfCad.AutoCAD;

internal static class SheetRuntimeSettings
{
    private static readonly object Gate = new();
    private static SheetPageBounds? _bounds;

    public static void SetBounds(SheetPageBounds bounds)
    {
        if (bounds is null) throw new ArgumentNullException(nameof(bounds));
        lock (Gate) _bounds = bounds;
    }

    public static bool TryGetBounds(out SheetPageBounds bounds)
    {
        lock (Gate)
        {
            if (_bounds is null)
            {
                bounds = null!;
                return false;
            }

            bounds = _bounds;
            return true;
        }
    }
}
