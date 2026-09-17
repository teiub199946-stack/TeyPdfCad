namespace TeyPdfCad.TestGenerator.Generation;

/// <summary>
/// Small deterministic SplitMix64 generator. Unlike System.Random, its sequence is
/// intentionally fixed here so a seed is reproducible across runtime versions.
/// </summary>
public sealed class StableRandom
{
    private ulong _state;

    public StableRandom(int seed)
    {
        _state = unchecked((uint)seed) + 0x9E3779B97F4A7C15UL;
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        return (int)(NextDouble() * exclusiveMax);
    }

    public double NextRange(double minInclusive, double maxExclusive)
    {
        return minInclusive + ((maxExclusive - minInclusive) * NextDouble());
    }

    public bool NextBool(double probability = 0.5)
    {
        return NextDouble() < probability;
    }
}
