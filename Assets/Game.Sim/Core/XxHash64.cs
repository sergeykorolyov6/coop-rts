using System;
using System.Buffers.Binary;

namespace Game.Sim;

// XXH64 (github.com/Cyan4973/xxHash). Written out because netstandard2.1, and so
// Unity, has no System.IO.Hashing.
public static class XxHash64
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    public static ulong Compute(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        unchecked
        {
            int offset = 0;
            ulong hash;

            if (data.Length >= 32)
            {
                ulong v1 = seed + Prime1 + Prime2;
                ulong v2 = seed + Prime2;
                ulong v3 = seed;
                ulong v4 = seed - Prime1;

                while (offset <= data.Length - 32)
                {
                    v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset)));
                    v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8)));
                    v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 16)));
                    v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 24)));
                    offset += 32;
                }

                hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                hash = MergeRound(hash, v1);
                hash = MergeRound(hash, v2);
                hash = MergeRound(hash, v3);
                hash = MergeRound(hash, v4);
            }
            else
            {
                hash = seed + Prime5;
            }

            hash += (ulong)data.Length;

            while (offset <= data.Length - 8)
            {
                hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset)));
                hash = RotateLeft(hash, 27) * Prime1 + Prime4;
                offset += 8;
            }

            if (offset <= data.Length - 4)
            {
                hash ^= BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset)) * Prime1;
                hash = RotateLeft(hash, 23) * Prime2 + Prime3;
                offset += 4;
            }

            while (offset < data.Length)
            {
                hash ^= data[offset] * Prime5;
                hash = RotateLeft(hash, 11) * Prime1;
                offset++;
            }

            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            return hash;
        }
    }

    private static ulong Round(ulong accumulator, ulong input)
    {
        unchecked
        {
            accumulator += input * Prime2;
            accumulator = RotateLeft(accumulator, 31);
            return accumulator * Prime1;
        }
    }

    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        unchecked
        {
            accumulator ^= Round(0, value);
            return accumulator * Prime1 + Prime4;
        }
    }

    private static ulong RotateLeft(ulong value, int bits) => (value << bits) | (value >> (64 - bits));
}
