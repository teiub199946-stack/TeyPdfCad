using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Primitives;

public sealed record LinePrimitive(Point2 Start, Point2 End, string? Layer = null);
