using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Generation;

public sealed class DimensionCaseGenerator
{
    private static readonly double[] FixedValues =
    {
        10, 25, 50, 100, 250, 500, 1000, 1200, 1800, 2200, 3000, 5200, 6000, 10000, 15000, 30000
    };

    private static readonly double[] Angles =
    {
        0, 1, 5, 10, 15, 30, 45, 60, 75, 89, 90, 91, 105, 135, 170, 179
    };

    private static readonly double[] Scales =
    {
        1, 2, 5, 10, 20, 25, 50, 100, 200, 500
    };

    private static readonly TextPlacement[] TextPlacements = Enum.GetValues<TextPlacement>();
    private static readonly ArrowType[] ArrowTypes = Enum.GetValues<ArrowType>();
    private static readonly double[] NoiseLevels = { 0, 0.001, 0.01, 0.05, 0.1, 0.5 };
    private static readonly NegativePattern[] NegativePatterns =
        Enum.GetValues<NegativePattern>().Where(x => x != NegativePattern.None).ToArray();

    private static readonly string[] AdversarialTags =
    {
        "very-short",
        "very-long",
        "almost-vertical",
        "almost-horizontal",
        "text-far",
        "flipped-text",
        "tiny-arrows",
        "huge-arrows",
        "uneven-extension-lines",
        "broken-dimension-line",
        "dimension-outside-geometry",
        "dimension-inside-geometry",
        "nearby-dimensions",
        "intersecting-dimensions"
    };

    public TestCorpus Generate(int count, int seed)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Case count must be positive.");
        }

        var cases = new List<DimensionCase>(count);
        var nextIndex = 0;

        foreach (var pattern in NegativePatterns)
        {
            if (nextIndex >= count)
            {
                break;
            }

            cases.Add(BuildNegative(nextIndex, seed, pattern));
            nextIndex++;
        }

        foreach (var chainLength in new[] { 2, 3, 5, 10, 20 })
        {
            if (nextIndex >= count)
            {
                break;
            }

            cases.Add(BuildChain(nextIndex, seed, chainLength));
            nextIndex++;
        }

        foreach (var tag in AdversarialTags)
        {
            if (nextIndex >= count)
            {
                break;
            }

            cases.Add(BuildAdversarial(nextIndex, seed, tag));
            nextIndex++;
        }

        while (nextIndex < count)
        {
            cases.Add(BuildRegular(nextIndex, seed));
            nextIndex++;
        }

        return new TestCorpus
        {
            Seed = seed,
            Cases = cases
        };
    }

    private static DimensionCase BuildRegular(int index, int globalSeed)
    {
        var caseSeed = DeriveCaseSeed(globalSeed, index);
        var rng = new StableRandom(caseSeed);

        var value = index % 4 == 0
            ? Math.Round(rng.NextRange(10, 30001), 0)
            : FixedValues[index % FixedValues.Length];

        var angle = Angles[(index / 2) % Angles.Length];
        var scale = Scales[(index / 3) % Scales.Length];
        var placement = TextPlacements[(index / 5) % TextPlacements.Length];
        var arrow = ArrowTypes[(index / 7) % ArrowTypes.Length];
        var noise = NoiseLevels[(index / 11) % NoiseLevels.Length];

        var type = angle == 0 || angle == 90
            ? DimensionType.Linear
            : index % 3 == 0 ? DimensionType.Aligned : DimensionType.Rotated;

        return BuildPositive(
            index,
            caseSeed,
            value,
            angle,
            scale,
            placement,
            arrow,
            noise,
            type,
            new List<string> { "regular" });
    }

    private static DimensionCase BuildNegative(int index, int globalSeed, NegativePattern pattern)
    {
        var caseSeed = DeriveCaseSeed(globalSeed, index);
        var value = FixedValues[index % FixedValues.Length];
        var angle = Angles[index % Angles.Length];
        var scale = Scales[index % Scales.Length];

        var positive = BuildPositive(
            index,
            caseSeed,
            value,
            angle,
            scale,
            TextPlacement.Centered,
            ArrowType.ClosedFilled,
            NoiseLevels[index % NoiseLevels.Length],
            DimensionType.Negative,
            new List<string> { "negative", pattern.ToString() });

        return positive with
        {
            ExpectedResult = ExpectedResult.Rejected,
            ExpectedConfidenceClass = ConfidenceClass.None,
            ExpectedDimensions = 0,
            NegativePattern = pattern,
            Segments = new List<DimensionSegment>(),
            IsDimensionLineBroken = pattern is NegativePattern.LinesTextNoArrows or NegativePattern.ArrowsLineNoExtensionLines
        };
    }

    private static DimensionCase BuildChain(int index, int globalSeed, int segmentCount)
    {
        var caseSeed = DeriveCaseSeed(globalSeed, index);
        var rng = new StableRandom(caseSeed);
        var angle = segmentCount switch
        {
            2 => 0,
            3 => 90,
            5 => 30,
            10 => 60,
            _ => 15
        };

        var scale = Scales[index % Scales.Length];
        var start = new Point2D(rng.NextRange(1000, 5000), rng.NextRange(1000, 5000));
        var radians = DegreesToRadians(angle);
        var direction = new Point2D(Math.Cos(radians), Math.Sin(radians));
        var normal = new Point2D(-direction.Y, direction.X);

        var segments = new List<DimensionSegment>(segmentCount);
        var cursor = start;
        var total = 0.0;

        for (var i = 0; i < segmentCount; i++)
        {
            var value = FixedValues[(index + i + 7) % FixedValues.Length];
            var next = Add(cursor, Scale(direction, value));
            segments.Add(new DimensionSegment
            {
                ExpectedValue = value,
                P1 = cursor,
                P2 = next
            });
            total += value;
            cursor = next;
        }

        var dimLinePoint = Add(Midpoint(start, cursor), Scale(normal, 4.0 * scale));
        var textPosition = dimLinePoint;
        var noiseLevel = NoiseLevels[index % NoiseLevels.Length];
        var noise = BuildNoise(noiseLevel, rng);
        var observed = ApplyNoise(start, cursor, dimLinePoint, textPosition, noise, rng);

        return new DimensionCase
        {
            Id = CaseId(index),
            Seed = caseSeed,
            DimensionType = DimensionType.Chain,
            ExpectedValue = total,
            P1 = start,
            P2 = cursor,
            DimensionLinePoint = dimLinePoint,
            Rotation = angle,
            TextPosition = textPosition,
            TextPlacement = TextPlacement.Centered,
            TextHeight = 2.5 * scale,
            ArrowType = ArrowTypes[index % ArrowTypes.Length],
            ArrowSize = 2.5 * scale,
            ExtensionLineOffset = 1.5 * scale,
            ExtensionLineExtension = 1.25 * scale,
            DrawingScale = scale,
            ExpectedConfidenceClass = ConfidenceClass.High,
            ExpectedResult = ExpectedResult.Recognized,
            ExpectedDimensions = segmentCount,
            Noise = noise,
            ObservedGeometry = observed,
            Segments = segments,
            Tags = new List<string>
            {
                "chain",
                $"segments:{segmentCount}",
                angle == 0 ? "horizontal" : angle == 90 ? "vertical" : "inclined"
            }
        };
    }

    private static DimensionCase BuildAdversarial(int index, int globalSeed, string tag)
    {
        var caseSeed = DeriveCaseSeed(globalSeed, index);
        var value = tag switch
        {
            "very-short" => 5,
            "very-long" => 50000,
            _ => FixedValues[(index + 3) % FixedValues.Length]
        };

        var angle = tag switch
        {
            "almost-vertical" => 89.9,
            "almost-horizontal" => 0.1,
            _ => Angles[index % Angles.Length]
        };

        var scale = Scales[(index + 2) % Scales.Length];
        var placement = tag is "text-far" or "dimension-outside-geometry"
            ? TextPlacement.OutsideRight
            : TextPlacements[index % TextPlacements.Length];

        var result = BuildPositive(
            index,
            caseSeed,
            value,
            angle,
            scale,
            placement,
            ArrowTypes[index % ArrowTypes.Length],
            NoiseLevels[(index + 3) % NoiseLevels.Length],
            angle == 0 || angle == 90 ? DimensionType.Linear : DimensionType.Aligned,
            new List<string> { "adversarial", tag });

        return result with
        {
            IsTextFlipped = tag == "flipped-text",
            IsDimensionLineBroken = tag == "broken-dimension-line",
            IsInsideGeometry = tag == "dimension-inside-geometry",
            ArrowSize = tag switch
            {
                "tiny-arrows" => 0.25 * scale,
                "huge-arrows" => 10.0 * scale,
                _ => result.ArrowSize
            },
            ExtensionLineExtension = tag == "uneven-extension-lines"
                ? result.ExtensionLineExtension * 3.0
                : result.ExtensionLineExtension,
            ExpectedDimensions = tag is "nearby-dimensions" or "intersecting-dimensions" ? 2 : 1
        };
    }

    private static DimensionCase BuildPositive(
        int index,
        int caseSeed,
        double value,
        double angle,
        double scale,
        TextPlacement placement,
        ArrowType arrow,
        double noiseLevel,
        DimensionType type,
        List<string> tags)
    {
        var rng = new StableRandom(caseSeed);
        var radians = DegreesToRadians(angle);
        var direction = new Point2D(Math.Cos(radians), Math.Sin(radians));
        var normal = new Point2D(-direction.Y, direction.X);
        var p1 = new Point2D(rng.NextRange(500, 10000), rng.NextRange(500, 10000));
        var p2 = Add(p1, Scale(direction, value));
        var dimLinePoint = Add(Midpoint(p1, p2), Scale(normal, 3.5 * scale));
        var textHeight = 2.5 * scale;
        var textPosition = PositionText(p1, p2, dimLinePoint, normal, textHeight, placement);
        var noise = BuildNoise(noiseLevel, rng);
        var observed = ApplyNoise(p1, p2, dimLinePoint, textPosition, noise, rng);

        return new DimensionCase
        {
            Id = CaseId(index),
            Seed = caseSeed,
            DimensionType = type,
            ExpectedValue = value,
            P1 = p1,
            P2 = p2,
            DimensionLinePoint = dimLinePoint,
            Rotation = angle,
            TextPosition = textPosition,
            TextPlacement = placement,
            TextHeight = textHeight,
            ArrowType = arrow,
            ArrowSize = 2.5 * scale,
            ExtensionLineOffset = 1.5 * scale,
            ExtensionLineExtension = 1.25 * scale,
            DrawingScale = scale,
            ExpectedConfidenceClass = noiseLevel >= 0.5 ? ConfidenceClass.Medium : ConfidenceClass.High,
            ExpectedResult = ExpectedResult.Recognized,
            ExpectedDimensions = 1,
            Noise = noise,
            ObservedGeometry = observed,
            Tags = tags
        };
    }

    private static NoiseSpec BuildNoise(double level, StableRandom rng)
    {
        if (level <= 0)
        {
            return new NoiseSpec();
        }

        return new NoiseSpec
        {
            CoordinateJitter = level,
            MicroBreak = rng.NextBool(0.30),
            EndpointMismatch = rng.NextBool(0.35),
            AngularSkewDegrees = rng.NextRange(-level * 0.2, level * 0.2),
            TextOffset = new Point2D(rng.NextRange(-level, level), rng.NextRange(-level, level))
        };
    }

    private static ObservedGeometry ApplyNoise(
        Point2D p1,
        Point2D p2,
        Point2D dimLinePoint,
        Point2D textPosition,
        NoiseSpec noise,
        StableRandom rng)
    {
        var jitter = noise.CoordinateJitter;

        Point2D Jitter(Point2D point)
        {
            if (jitter <= 0)
            {
                return point;
            }

            return new Point2D(
                point.X + rng.NextRange(-jitter, jitter),
                point.Y + rng.NextRange(-jitter, jitter));
        }

        var noisyText = Jitter(textPosition);
        noisyText = Add(noisyText, noise.TextOffset);

        return new ObservedGeometry
        {
            P1 = Jitter(p1),
            P2 = Jitter(p2),
            DimensionLinePoint = Jitter(dimLinePoint),
            TextPosition = noisyText
        };
    }

    private static Point2D PositionText(
        Point2D p1,
        Point2D p2,
        Point2D dimLinePoint,
        Point2D normal,
        double textHeight,
        TextPlacement placement)
    {
        var along = placement switch
        {
            TextPlacement.OffsetLeft => 0.35,
            TextPlacement.OffsetRight => 0.65,
            TextPlacement.OutsideLeft => -0.15,
            TextPlacement.OutsideRight => 1.15,
            _ => 0.50
        };

        var perpendicular = placement switch
        {
            TextPlacement.Above => textHeight,
            TextPlacement.Below => -textHeight,
            _ => 0
        };

        var linePoint = Add(p1, Scale(Subtract(p2, p1), along));
        var offsetFromBaseline = Subtract(dimLinePoint, Midpoint(p1, p2));
        return Add(Add(linePoint, offsetFromBaseline), Scale(normal, perpendicular));
    }

    private static int DeriveCaseSeed(int globalSeed, int index)
    {
        unchecked
        {
            var value = ((uint)globalSeed * 397U) ^ (uint)(index + 1);
            value ^= value >> 16;
            value *= 0x7FEB352DU;
            value ^= value >> 15;
            value *= 0x846CA68BU;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private static string CaseId(int index) => $"case_{index + 1:000000}";

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static Point2D Add(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);

    private static Point2D Subtract(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);

    private static Point2D Scale(Point2D point, double scale) => new(point.X * scale, point.Y * scale);

    private static Point2D Midpoint(Point2D a, Point2D b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
}
