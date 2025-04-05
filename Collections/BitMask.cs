using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using CodexECS.Utility;

namespace CodexECS
{
    using MaskInternal = UInt32;

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

        //0 hash is dirty by default, I hope it will never calculate actual hash to 0 :)
        private int _hash;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMaskHash()
        {
            if (_hash != 0 || Length == 0)
                return _hash;
            
            var hash = (int)(17 * 23 * _m1);
            var length = GetDynamicChunksLength(Length);
            for (int i = 0; i < length; ++i)
                hash = hash * 23 + (int)_mn[i];
            _hash = hash;

#if DEBUG
            if (Length > 0 && _hash == 0)
                throw new EcsException("actual value of BitMask hash is 0");
#endif
            
            return _hash;
        }

        public const int SizeOfPartInBits = sizeof(MaskInternal) * 8;
        private MaskInternal _m1;
        private MaskInternal[] _mn;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        public BitMask(params int[] positions)
        {
            _m1 = 0;
            _mn = null;
            Length = 0;
            _hash = 0;

            Set(positions);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetDynamicChunksLength(int length) => (int)Math.Ceiling((float)length / SizeOfPartInBits) - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in BitMask other)
        {
            _m1 = other._m1;
            Length = other.Length;

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
        public void Set(int i)
        {
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
            m |= (MaskInternal)(shifted << position);

            //update length
            i++;
            if (Length < i)
                Length = i;
            
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
            var intersects = _m1 & otherMask._m1;
            if (_mn == null || otherMask._mn == null)
                return intersects > 0;

            if (_mn.Length < otherMask._mn.Length)
            {
                for (int i = 0; i < _mn.Length; ++i)
                    intersects &= _mn[i] & otherMask._mn[i];
            }
            else
            {
                for (int i = 0; i < otherMask._mn.Length; ++i)
                    intersects &= otherMask._mn[i] & _mn[i];
            }

            return intersects > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckChunkIdx(int idx) => idx > 0 && (_mn == null || _mn.Length <= idx - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(int i)
        {
            int chunkIdx = i / SizeOfPartInBits;
            if (CheckChunkIdx(chunkIdx))
                return;

            ref var m = ref _m1;
            if (chunkIdx > 0)
                m = ref _mn[chunkIdx - 1];

            int position = i % SizeOfPartInBits;
            MaskInternal shifted = 1;
            m &= (MaskInternal)~(shifted << position);

            //update length
            if (chunkIdx == (Length - 1) / SizeOfPartInBits)
            {
                int j = chunkIdx - 1;
                var msb = 0;
                for (; j >= 0; j--)
                {
                    if (_mn[j] == 0)
                        continue;
                    var chunk = _mn[j];
                    while (chunk != 0)
                    {
                        chunk >>= 1;
                        msb++;
                    }
                    break;
                }
                if (j < 0)
                {
                    var chunk = _m1;
                    while (chunk != 0)
                    {
                        chunk >>= 1;
                        msb++;
                    }
                }

                j++;
                Length = j * SizeOfPartInBits + msb;
            }
            
            _hash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Check(int i)
        {
            int chunkIdx = i / SizeOfPartInBits;
            if (CheckChunkIdx(chunkIdx))
                return false;

            var m = _m1;
            if (chunkIdx > 0)
                m = _mn[chunkIdx - 1];
            int position = i % SizeOfPartInBits;
            return (m & (1 << position)) != 0;
        }

        public int GetNextSetBit(int fromPosition)
        {
            for (int i = fromPosition; i < Length; i++)
            {
                int chunkIdx = i / SizeOfPartInBits;
                if (CheckChunkIdx(chunkIdx))
                    return -1;

                var m = _m1;
                if (chunkIdx > 0)
                    m = _mn[chunkIdx - 1];

                for (int j = i % SizeOfPartInBits; j < SizeOfPartInBits; j++)
                {
                    if ((m & (1 << j)) != 0)
                        return j + (chunkIdx * SizeOfPartInBits);
                }
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
                if (_nextSetBit == -1)
                    _nextSetBit = _bitMask.GetNextSetBit(0);
                else
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
            {
                for (int i = 0; i < _mn.Length; i++)
                    _mn[i] = 0;
            }
            
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

            var chunksCount = GetDynamicChunksLength(filter.Length);
            for (int i = 0; i < chunksCount; i++)
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
                var chunksCount = GetDynamicChunksLength(filter.Length);
                for (int i = 0; i < chunksCount && i < _mn.Length; i++)
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
                int length = GetDynamicChunksLength(Length);
                for (int i = 0; i < length; i++)
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
