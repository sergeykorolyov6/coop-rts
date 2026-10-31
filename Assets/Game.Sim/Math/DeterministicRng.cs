using System;

namespace Game.Sim;

// PCG32 (pcg-random.org, pcg32_srandom_r / pcg32_random_r / pcg32_boundedrand_r).
// The only source of randomness in the sim; its state is part of the match state and the StateHash.
public sealed class DeterministicRng
{
    private const ulong Multiplier = 6364136223846793005UL;

    // Fixed stream id: every match uses the same stream, the seed alone picks the sequence.
    private const ulong StreamId = 54;

    private ulong _state;
    private readonly ulong _increment;

    public DeterministicRng(int seed)
    {
        _increment = (StreamId << 1) | 1UL;
        _state = 0;
        NextUInt();
        _state += (ulong)(long)seed;
        NextUInt();
    }

    public ulong State => _state;

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
    }

    // Uniform in [0, maxExclusive) without modulo bias.
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");

        uint bound = (uint)maxExclusive;
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint value = NextUInt();
            if (value >= threshold)
                return (int)(value % bound);
        }
    }

    // Uniform in [minInclusive, maxExclusive).
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be greater than minInclusive.");

        long range = (long)maxExclusive - minInclusive;
        if (range > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Range does not fit in int.");

        return minInclusive + NextInt((int)range);
    }
}
