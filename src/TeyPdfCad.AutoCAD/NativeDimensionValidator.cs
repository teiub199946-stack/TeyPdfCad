using Autodesk.AutoCAD.DatabaseServices;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.AutoCAD;

internal sealed class NativeDimensionValidator
{
    public NativeDimensionValidationResult Validate(
        Transaction transaction,
        IReadOnlyList<ObjectId> createdIds,
        IReadOnlyList<DimensionCandidate> candidates,
        double maxRelativeError = 0.005)
    {
        if (createdIds.Count != candidates.Count)
            return new NativeDimensionValidationResult(false, double.PositiveInfinity, "Created object count does not match semantic candidate count.");

        var maxError = 0.0;

        for (var i = 0; i < createdIds.Count; i++)
        {
            if (transaction.GetObject(createdIds[i], OpenMode.ForRead, false) is not Dimension dimension)
                return new NativeDimensionValidationResult(false, double.PositiveInfinity, $"Object {createdIds[i]} is not an AutoCAD Dimension.");

            var expected = candidates[i].DisplayedValue;
            var actual = dimension.Measurement;
            var relativeError = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1.0);
            maxError = Math.Max(maxError, relativeError);

            if (!double.IsFinite(actual) || relativeError > maxRelativeError)
            {
                return new NativeDimensionValidationResult(
                    false,
                    maxError,
                    $"Native measurement mismatch: expected {expected:G17}, actual {actual:G17}, rel.error {relativeError:P4}.");
            }
        }

        return new NativeDimensionValidationResult(true, maxError, null);
    }
}

internal sealed record NativeDimensionValidationResult(
    bool Success,
    double MaxRelativeError,
    string? ErrorMessage);
