using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodexECS.Utility;

namespace CodexECS
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PackedIndices
    {
        //16 max
        public int i1;
        public int i2;
        public int i3;
        public int i4;
        public int i5;
        public int i6;
        public int i7;
        public int i8;
        public int i9;
        public int i10;
        public int i11;
        public int i12;
        // public int i13;
        // public int i14;
        // public int i15;
        // public int i16;
    }

    public static class PackedIndicesBuffer
    {
        private const int BUFFER_START_SIZE = 1024;
        private static PackedIndices[] _indices = new PackedIndices[BUFFER_START_SIZE];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PackedIndices[] GetBuffer(int size)
        {
            if (size <= _indices.Length)
                return _indices;
            
            const int maxResizeDelta = 256;
            Utils.ResizeArray(size - 1, ref _indices, maxResizeDelta);
            return _indices;
        }
    }
}