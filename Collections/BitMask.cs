using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
#if BITMASK_USE_BITOPERATIONS
using System.Numerics;
#endif
using CodexECS.Utility;

namespace CodexECS
{
    using MaskInternal = UInt32;

    public static class BitMaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMask SetTypeId<T>(this BitMask mask)
        {
            mask.Set(ComponentMeta<T>.Id);
            return mask;
        }
    }

    public struct BitMask
    {
        public class EqualityComparer : IEqualityComparer<BitMask>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(BitMask x, BitMask y) => x.MasksEquals(y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode(BitMask obj) => obj.GetMaskHash();
        }

        public static readonly EqualityComparer MaskComparer;
        static BitMask() => MaskComparer = new();

        private int _hash;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMaskHash()
        {
            if (_hash != 0)
                return _hash;
    
            if (Length == 0)
                return 0;
            
            var hash = (int)(17 * 23 * _m1);
            var dynamicChunksLength = GetDynamicChunksLength(_length);
            for (int i = 0; i < dynamicChunksLength; ++i)
                hash = hash * 23 + (int)_mn[i];

            _hash = hash == 0 ? 1 : hash;
            return _hash;
        }

        public const int SizeOfPartInBits = sizeof(MaskInternal) * 8;
        private MaskInternal _m1;
        private MaskInternal[] _mn;

        private int _length;
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        private int _setBitsCount;
        public int SetBitsCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _setBitsCount;
        }

        public BitMask(params int[] positions)
        {
            _m1 = 0;
            _mn = null;
            _length = 0;
            _hash = 0;
            _setBitsCount = 0;

            Set(positions);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDynamicChunksLength(int length)
        {
            var chunkSizeInBits = sizeof(MaskInternal) * 8;
            if (length <= chunkSizeInBits)
                return 0;

            return ((length + chunkSizeInBits - 1) / chunkSizeInBits) - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountSetBits(MaskInternal value)
        {
#if BITMASK_USE_BITOPERATIONS
            return BitOperations.PopCount(value);
#else
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetHighestSetBitIndex(MaskInternal value)
        {
#if BITMASK_USE_BITOPERATIONS
            return value == 0 ? -1 : (SizeOfPartInBits - 1 - BitOperations.LeadingZeroCount(value));
#else
            if (value == 0)
                return -1;

            int msb = SizeOfPartInBits - 1;
            MaskInternal mask = (MaskInternal)1 << msb;
            while ((mask & value) == 0 && msb > 0)
            {
                msb--;
                mask >>= 1;
            }

            return msb;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetLowestSetBitIndex(MaskInternal value)
        {
#if BITMASK_USE_BITOPERATIONS
            return value == 0 ? -1 : BitOperations.TrailingZeroCount(value);
#else
            if (value == 0)
                return -1;

            int bit = 0;
            while ((value & 1) == 0)
            {
                value >>= 1;
                bit++;
            }

            return bit;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecalculateLengthAndSetBitsCount()
        {
            var setBitsCount = CountSetBits(_m1);
            int length = _m1 == 0 ? 0 : GetHighestSetBitIndex(_m1) + 1;

            if (_mn != null)
            {
                for (int i = 0; i < _mn.Length; i++)
                {
                    var chunk = _mn[i];
                    if (chunk == 0)
                        continue;

                    setBitsCount += CountSetBits(chunk);
                    length = (i + 1) * SizeOfPartInBits + GetHighestSetBitIndex(chunk) + 1;
                }
            }

            _setBitsCount = setBitsCount;
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in BitMask other)
        {
            _m1 = other._m1;
            _length = other._length;
            _setBitsCount = other._setBitsCount;

            var otherArrLength = other._mn != null ? other._mn.Length : 0;
            if (otherArrLength > 0)
            {
                if (_mn == null || _mn.Length < otherArrLength)
                    _mn = new MaskInternal[other._mn.Length];
                for (int i = 0; i < other._mn.Length; i++)
                    _mn[i] = other._mn[i];
                for (int i = other._mn.Length; i < _mn.Length; i++)
                    _mn[i] = 0;
            }
            else if (_mn != null)
            {
                for (int i = 0; i < _mn.Length; i++)
                    _mn[i] = 0;
            }

            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask Duplicate()
        {
            var copy = new BitMask();
            copy.Copy(this);
            return copy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(params int[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
                Set(positions[i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(in BitMask other)
        {
            _m1 |= other._m1;

            var otherMnLength = other._mn != null ? other._mn.Length : 0;
            if (otherMnLength > 0)
            {
                if (_mn == null || _mn.Length < otherMnLength)
                {
                    const int maxResizeDelta = 8;
                    Utils.ResizeArray(otherMnLength, ref _mn, maxResizeDelta);
                }

                for (int i = 0; i < otherMnLength; i++)
                    _mn[i] |= other._mn[i];
            }

            RecalculateLengthAndSetBitsCount();
            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int i)
        {
            if (i < 0)
                return;

            var chunkIdx = i / SizeOfPartInBits;
            ref var m = ref _m1;
            if (chunkIdx > 0)
            {
                chunkIdx--;
                //resize if needed
                if (_mn == null || _mn.Length <= chunkIdx)
                {
                    const int maxResizeDelta = 8;
                    Utils.ResizeArray(chunkIdx, ref _mn, maxResizeDelta);
                }
                m = ref _mn[chunkIdx];
            }

            int position = i % SizeOfPartInBits;
            MaskInternal shifted = 1;
            shifted <<= position;

            if ((m & shifted) == 0)
                _setBitsCount++;

            m |= shifted;

            //update length
            i++;
            if (_length < i)
                _length = i;
            
            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask And(int i)
        {
            var mask = Duplicate();
            mask.Set(i);
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask And(params int[] positions)
        {
            var mask = Duplicate();
            for (int i = 0; i < positions.Length; i++)
                mask.Set(positions[i]);
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask AndNot(int i)
        {
            var mask = Duplicate();
            mask.Unset(i);
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitMask AndNot(params int[] positions)
        {
            var mask = Duplicate();
            for (int i = 0; i < positions.Length; i++)
                mask.Unset(positions[i]);
            return mask;
        }
        
        public bool Intersects(in BitMask otherMask)
        {
            if ((_m1 & otherMask._m1) != 0)
                return true;
            var otherMn = otherMask._mn;
            if (_mn == null || otherMn == null)
                return false;
            var minLength = _mn.Length < otherMn.Length ? _mn.Length : otherMn.Length;
    
            for (int i = 0; i < minLength; ++i)
            {
                if ((_mn[i] & otherMn[i]) != 0)
                    return true;
            }
    
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckChunkIdx(int idx) => idx < 1 || (_mn != null && _mn.Length > idx - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(in BitMask other)
        {
            _m1 &= ~other._m1;

            var otherMnLength = other._mn != null ? other._mn.Length : 0;
            if (_mn != null && otherMnLength > 0)
            {
                int minLength = _mn.Length < otherMnLength ? _mn.Length : otherMnLength;
                for (int i = 0; i < minLength; i++)
                    _mn[i] &= ~other._mn[i];
            }   

            RecalculateLengthAndSetBitsCount();
            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(int i)
        {
            if (i < 0)
                return;

            int chunkIdx = i / SizeOfPartInBits;
            if (!CheckChunkIdx(chunkIdx))
                return;

            ref var m = ref _m1;
            if (chunkIdx > 0)
                m = ref _mn[chunkIdx - 1];

            int position = i % SizeOfPartInBits;
            MaskInternal shifted = (MaskInternal)1 << position;
            bool wasSet = (m & shifted) != 0;
            m &= ~shifted;

            if (wasSet)
            {
                _setBitsCount--;

#if DEBUG && !ECS_PERF_TEST
                if (_setBitsCount < 0)
                    throw new EcsException("negative set bits count");
#endif

                // RecalculateLength
                if (i == Length - 1)
                {
                    if (_mn != null)
                    {
                        for (int j = _mn.Length - 1; j >= 0; j--)
                        {
                            if (_mn[j] == 0) continue;
                            int msb = GetHighestSetBitIndex(_mn[j]);
                            _length = (j + 1) * SizeOfPartInBits + msb + 1;
                            return;
                        }
                    }

                    // Check _m1
                    if (_m1 != 0)
                    {
                        int msb = GetHighestSetBitIndex(_m1);
                        _length = msb + 1;
                        return;
                    }

                    // No bits set
                    _length = 0;
                }
            }
    
            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Check(int i)
        {
            if (i < 0)
                return false;

            int chunkIdx = i / SizeOfPartInBits;
            if (!CheckChunkIdx(chunkIdx))
                return false;

            var m = _m1;
            if (chunkIdx > 0)
                m = _mn[chunkIdx - 1];
            int position = i % SizeOfPartInBits;
            return (m & (1 << position)) != 0;
        }

        public int GetNextSetBit(int fromPosition)
        {
            if (fromPosition < 0)
                fromPosition = 0;

            if (fromPosition >= Length)
                return -1;

            int chunkIdx = fromPosition / SizeOfPartInBits;
            int bitOffset = fromPosition % SizeOfPartInBits;

            while (chunkIdx * SizeOfPartInBits < Length)
            {
                MaskInternal chunk;
                if (chunkIdx == 0)
                {
                    chunk = _m1;
                }
                else
                {
                    int dynamicChunkIdx = chunkIdx - 1;
                    if (_mn == null || dynamicChunkIdx >= _mn.Length)
                        return -1;

                    chunk = _mn[dynamicChunkIdx];
                }

                if (bitOffset > 0)
                {
                    MaskInternal lowerBitsMask = ((MaskInternal)1 << bitOffset) - 1;
                    chunk &= ~lowerBitsMask;
                }

                if (chunk != 0)
                {
                    int bit = GetLowestSetBitIndex(chunk);
                    return bit + (chunkIdx * SizeOfPartInBits);
                }

                chunkIdx++;
                bitOffset = 0;
            }

            return -1;
        }
        
        #region Enumerable
        //CODEX_TODO: possible optimization on for iteration instead of foreach
        public struct Enumerator
        {
            private int _nextSetBit;
            private BitMask _bitMask;

            public Enumerator(BitMask bitMask)
            {
                _bitMask = bitMask;
                _nextSetBit = -1;
            }

            public int Current => _nextSetBit;

            public bool MoveNext()
            {
                _nextSetBit = _bitMask.GetNextSetBit(_nextSetBit + 1);
                return _nextSetBit != -1;
            }
        }

        public Enumerator GetEnumerator() => new(this);
        
        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _m1 = 0;
            if (_mn != null)
                Array.Clear(_mn, 0, _mn.Length);

            _length = 0;
            _setBitsCount = 0;
            _hash = 0;
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private bool InclusivePass_Internal(MaskInternal value, MaskInternal filter) => (filter & (value ^ filter)) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InclusivePass(in BitMask filter)
        {
            if (filter.Length > Length)
                return false;
            if ((filter._m1 & (_m1 ^ filter._m1)) != 0)
            //if (!InclusivePass_Internal(_m1, filter._m1))
                return false;

            var dynamicChunksLength = GetDynamicChunksLength(filter._length);
            for (int i = 0; i < dynamicChunksLength; i++)
            {
                var filterChunk = filter._mn[i];
                if (filterChunk == 0)
                    continue;

                if ((filterChunk & (_mn[i] ^ filterChunk)) != 0)
                //if (!InclusivePass_Internal(_mn[i], filterChunk))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ExclusivePass(in BitMask filter)
        {
            if ((filter._m1 & _m1) != 0)
                return false;
            if (filter._mn != null && _mn != null)
            {
                var dynamicChunksLength = GetDynamicChunksLength(filter._length);
                for (int i = 0; i < dynamicChunksLength && i < _mn.Length; i++)
                {
                    if ((filter._mn[i] & _mn[i]) != 0)
                        return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MasksEquals(in BitMask other)
        {
            if (Length != other.Length)
                return false;

            if (_m1 != other._m1)
                return false;

            if (_mn != null)
            {
                var dynamicChunksLength = GetDynamicChunksLength(_length);
                for (int i = 0; i < dynamicChunksLength; i++)
                {
                    if (_mn[i] != other._mn[i])
                        return false;
                }
            }
            
            return true;
        }

#if DEBUG && !ECS_PERF_TEST
        public override string ToString()
        {
            if (Length == 0)
                return "{ }";
            
            var sb = new StringBuilder();
            sb.Append("{ ");
            foreach (var bit in this)
                sb.Append(bit).Append(", ");
            sb.Remove(sb.Length - 2, 2);
            sb.Append(" }");
            
            // if (_mn != null)
            //     for (int i = _mn.Length - 1; i > -1; i--)
            //         sb.Append(Convert.ToString(_mn[i], 2).PadLeft(SizeOfPartInBits, '0'));
            // sb.Append(Convert.ToString(_m1, 2).PadLeft(SizeOfPartInBits, '0'));
            // sb.Append(". Length: " + Length);

            return sb.ToString();
        }

        public string ChunkToString(MaskInternal chunk) => Convert.ToString(chunk, 2).PadLeft(SizeOfPartInBits, '0');

        public void SetBits(int[] bits)
        {
            int j = 0;
            for (int i = bits.Length - 1; i >= 0; i--, j++)
            {
                if (bits[i] != 0)
                    Set(j);
            }
        }
#endif
    }
}
