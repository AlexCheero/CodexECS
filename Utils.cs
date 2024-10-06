
using System;
using System.Runtime.CompilerServices;

namespace CodexECS.Utility
{
    public static class Utils
    {
        private static readonly int[] BITPatternToLog2 =
        {
            0, // change to 1 if you want bitSize(0) = 1
            48, -1, -1, 31, -1, 15, 51, -1, 63, 5, -1, -1, -1, 19, -1, 
            23, 28, -1, -1, -1, 40, 36, 46, -1, 13, -1, -1, -1, 34, -1, 58,
            -1, 60, 2, 43, 55, -1, -1, -1, 50, 62, 4, -1, 18, 27, -1, 39, 
            45, -1, -1, 33, 57, -1, 1, 54, -1, 49, -1, 17, -1, -1, 32, -1,
            53, -1, 16, -1, -1, 52, -1, -1, -1, 64, 6, 7, 8, -1, 9, -1, 
            -1, -1, 20, 10, -1, -1, 24, -1, 29, -1, -1, 21, -1, 11, -1, -1,
            41, -1, 25, 37, -1, 47, -1, 30, 14, -1, -1, -1, -1, 22, -1, -1,
            35, 12, -1, -1, -1, 59, 42, -1, -1, 61, 3, 26, 38, 44, -1, 56
        };

        private const ulong MULTIPLICATOR = 0x6c04f118e9966f6bUL;

        //De Brujin
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BITSize(ulong v)
        {
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v |= v >> 32;
            return BITPatternToLog2[v * MULTIPLICATOR >> 57];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BITSize(int v)
        {
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            //v |= v >> 32;
            return BITPatternToLog2[(uint)v * MULTIPLICATOR >> 57];
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int GetNextPOT(int value) => 1 << (BITSize(value) + 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResizeArray<T>(int lastIdx, ref T[] arr, int maxResizeDelta)
        {
            int newLength;
            if (arr == null || arr.Length < maxResizeDelta || arr.Length + maxResizeDelta <= lastIdx)
                newLength = 1 << (BITSize(lastIdx) + 1);
            else
                newLength = arr.Length + maxResizeDelta;

#if DEBUG
            if (newLength <= lastIdx)
                throw new IndexOutOfRangeException(
                    $"newLength ({newLength}) for array with length ({arr?.Length ?? 0}) is still smaller than lastIdx ({lastIdx}). maxResizeDelta ({maxResizeDelta})");
#endif
            
            if (arr == null)
                arr = new T[newLength];
            else
                Array.Resize(ref arr, newLength);
        }
    }
}