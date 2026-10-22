using System.Runtime.CompilerServices;
#if NETCOREAPP3_0_OR_GREATER && !ECS_PORTABLE_BITS
using System.Numerics;
#endif

namespace CodexECS
{
    // Storage geometry shared by membership, payload and queries.
    internal static class Layout
    {
        // A page is 64 slots: one membership word per component.
        internal const int PageShift = 6;
        // A chunk is 64 pages: one summary word per component.
        internal const int ChunkShift = 12;
        // Payload arrays cover 256 slots (4 pages). They are allocated on first use and never move.
        internal const int SegmentShift = 8;
        internal const int SegmentSize = 1 << SegmentShift;
        internal const int SegmentMask = SegmentSize - 1;
    }

    internal static class Bits
    {
#if !NETCOREAPP3_0_OR_GREATER || ECS_PORTABLE_BITS
        private static readonly byte[] DeBruijn =
        {
            0, 1, 2, 53, 3, 7, 54, 27, 4, 38, 41, 8, 34, 55, 48, 28,
            62, 5, 39, 46, 44, 42, 22, 9, 24, 35, 59, 56, 49, 18, 29, 11,
            63, 52, 6, 26, 37, 40, 33, 47, 61, 45, 43, 21, 23, 58, 17, 10,
            51, 25, 36, 32, 60, 20, 57, 16, 50, 31, 19, 15, 30, 14, 13, 12
        };
#endif

        // Index of the lowest set bit; 64 for zero.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int TrailingZeros(ulong value)
        {
#if NETCOREAPP3_0_OR_GREATER && !ECS_PORTABLE_BITS
            return BitOperations.TrailingZeroCount(value);
#else
            if (value == 0) return 64;
            return DeBruijn[((value & (0UL - value)) * 0x022FDD63CC95386DUL) >> 58];
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int PopCount(ulong value)
        {
#if NETCOREAPP3_0_OR_GREATER && !ECS_PORTABLE_BITS
            return BitOperations.PopCount(value);
#else
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
#endif
        }
    }
}

#if ENABLE_IL2CPP
namespace Unity.IL2CPP.CompilerServices
{
    // IL2CPP recognizes this attribute by name; the project has to declare it itself.
    internal enum Option
    {
        NullChecks = 1,
        ArrayBoundsChecks = 2,
        DivideByZeroChecks = 3,
    }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Method |
                           System.AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    internal sealed class Il2CppSetOptionAttribute : System.Attribute
    {
        public Option Option { get; }
        public object Value { get; }

        public Il2CppSetOptionAttribute(Option option, object value)
        {
            Option = option;
            Value = value;
        }
    }
}
#endif
