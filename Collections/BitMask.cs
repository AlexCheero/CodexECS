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
    using MaskInternal = Int64;

    public static class BitMaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMask SetTypeId<T>(this BitMask mask)
        {
            mask.Set(ComponentMeta<T>.Id);
            return mask;
        }
    }

    /// <summary>
    /// A mutable bit mask. Struct copies share storage; use Duplicate for an independent mask.
    /// </summary>
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
            
            var hash = 17;
            var partsLength = GetPartsLength(_length);
            for (int i = 0; i < partsLength; ++i)
                hash = unchecked(hash * 23 + _parts[i].GetHashCode());

            _hash = hash == 0 ? 1 : hash;
            return _hash;
        }

        public const int SizeOfPartInBits = sizeof(MaskInternal) * 8;
        private MaskInternal[] _parts;

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
            _parts = null;
            _length = 0;
            _hash = 0;
            _setBitsCount = 0;

            Set(positions);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPartsLength(int length)
        {
            return length == 0 ? 0 : (length - 1) / SizeOfPartInBits + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountSetBits(MaskInternal value)
        {
#if BITMASK_USE_BITOPERATIONS
            return BitOperations.PopCount(unchecked((ulong)value));
#else
            int count = 0;
            while (value != 0)
            {
                value &= unchecked(value - 1);
                count++;
            }

            return count;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetHighestSetBitIndex(MaskInternal value)
        {
#if BITMASK_USE_BITOPERATIONS
            return value == 0 ? -1 : (SizeOfPartInBits - 1 - BitOperations.LeadingZeroCount(unchecked((ulong)value)));
#else
            if (value == 0)
                return -1;

            int msb = SizeOfPartInBits - 1;
            // Shift an unsigned mask so bit 63 does not sign-extend.
            ulong mask = 1UL << msb;
            while ((mask & unchecked((ulong)value)) == 0 && msb > 0)
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
            return value == 0 ? -1 : BitOperations.TrailingZeroCount(unchecked((ulong)value));
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
            int setBitsCount = 0;
            int length = 0;

            if (_parts != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    var chunk = _parts[i];
                    if (chunk == 0)
                        continue;

                    setBitsCount += CountSetBits(chunk);
                    length = i * SizeOfPartInBits + GetHighestSetBitIndex(chunk) + 1;
                }
            }

            _setBitsCount = setBitsCount;
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in BitMask other)
        {
            _length = other._length;
            _setBitsCount = other._setBitsCount;

            var otherArrLength = other._parts != null ? other._parts.Length : 0;
            if (otherArrLength > 0)
            {
                if (_parts == null || _parts.Length < otherArrLength)
                    _parts = new MaskInternal[otherArrLength];
                Array.Copy(other._parts, _parts, otherArrLength);
                Array.Clear(_parts, otherArrLength, _parts.Length - otherArrLength);
            }
            else if (_parts != null)
            {
                Array.Clear(_parts, 0, _parts.Length);
            }

            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly BitMask Duplicate()
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
            var otherPartsLength = GetPartsLength(other._length);
            if (otherPartsLength > 0)
            {
                if (_parts == null || _parts.Length < otherPartsLength)
                {
                    const int maxResizeDelta = 8;
                    Utils.ResizeArray(otherPartsLength - 1, ref _parts, maxResizeDelta);
                }

                for (int i = 0; i < otherPartsLength; i++)
                    _parts[i] |= other._parts[i];
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
            if (_parts == null || _parts.Length <= chunkIdx)
            {
                const int maxResizeDelta = 8;
                Utils.ResizeArray(chunkIdx, ref _parts, maxResizeDelta);
            }

            ref var m = ref _parts[chunkIdx];
            int position = i % SizeOfPartInBits;
            MaskInternal shifted = 1L << position;

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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(in BitMask otherMask)
        {
            var minLength = GetPartsLength(Math.Min(_length, otherMask._length));
    
            for (int i = 0; i < minLength; ++i)
            {
                if ((_parts[i] & otherMask._parts[i]) != 0)
                    return true;
            }
    
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckChunkIdx(int idx) => _parts != null && idx >= 0 && idx < _parts.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(in BitMask other)
        {
            var minLength = GetPartsLength(Math.Min(_length, other._length));
            for (int i = 0; i < minLength; i++)
                _parts[i] &= ~other._parts[i];

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

            ref var m = ref _parts[chunkIdx];

            int position = i % SizeOfPartInBits;
            MaskInternal shifted = 1L << position;
            bool wasSet = (m & shifted) != 0;
            m &= ~shifted;
            _hash = 0;

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
                    for (int j = chunkIdx; j >= 0; j--)
                    {
                        if (_parts[j] == 0) continue;
                        int msb = GetHighestSetBitIndex(_parts[j]);
                        _length = j * SizeOfPartInBits + msb + 1;
                        return;
                    }

                    // No bits set
                    _length = 0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Check(int i)
        {
            if (i < 0)
                return false;

            int chunkIdx = i / SizeOfPartInBits;
            if (!CheckChunkIdx(chunkIdx))
                return false;

            var m = _parts[chunkIdx];
            int position = i % SizeOfPartInBits;
            return (m & (1L << position)) != 0;
        }

        public int GetNextSetBit(int fromPosition)
        {
            if (fromPosition < 0)
                fromPosition = 0;

            if (fromPosition >= Length)
                return -1;

            int chunkIdx = fromPosition / SizeOfPartInBits;
            int bitOffset = fromPosition % SizeOfPartInBits;

            var partsLength = GetPartsLength(_length);
            while (chunkIdx < partsLength)
            {
                MaskInternal chunk = _parts[chunkIdx] & (-1L << bitOffset);

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
            if (_parts != null)
                Array.Clear(_parts, 0, _parts.Length);

            _length = 0;
            _setBitsCount = 0;
            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool InclusivePass(in BitMask filter)
        {
            if (filter.Length > Length)
                return false;
            var partsLength = GetPartsLength(filter._length);
            for (int i = 0; i < partsLength; i++)
            {
                var filterChunk = filter._parts[i];
                if (filterChunk == 0)
                    continue;

                if ((filterChunk & ~_parts[i]) != 0)
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ExclusivePass(in BitMask filter)
        {
            var minLength = GetPartsLength(Math.Min(_length, filter._length));
            for (int i = 0; i < minLength; i++)
            {
                if ((filter._parts[i] & _parts[i]) != 0)
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MasksEquals(in BitMask other)
        {
            if (Length != other.Length)
                return false;

            var partsLength = GetPartsLength(_length);
            for (int i = 0; i < partsLength; i++)
            {
                if (_parts[i] != other._parts[i])
                    return false;
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
            
            return sb.ToString();
        }

        public string ChunkToString(uint chunk) => Convert.ToString(chunk, 2).PadLeft(sizeof(uint) * 8, '0');

        public string ChunkToString(long chunk) => Convert.ToString(chunk, 2).PadLeft(SizeOfPartInBits, '0');

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
