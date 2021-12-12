﻿using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace ECS
{
    using MaskInternal = UInt32;

    public struct BitMask
    {
        private const int SizeOfPartInBits = sizeof(MaskInternal) * 8;
        private MaskInternal _m1;
        private MaskInternal[] _mn;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        public BitMask(MaskInternal m1 = 0, MaskInternal[] mn = null)
        {
            _m1 = m1;
            _mn = mn;
            Length = 0;

            a = b = c = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in BitMask other)
        {
            _m1 = other._m1;
            Length = other.Length;
            var chunksLength = Length / SizeOfPartInBits;
            if (_mn == null || _mn.Length < Length)
            {
                var newChunksLength = 2;
                while (newChunksLength < chunksLength)
                    newChunksLength <<= 1;
                newChunksLength--;
                _mn = new MaskInternal[newChunksLength];
            }

            for (int i = 0; i < chunksLength; i++)
                _mn[i] = other._mn[i];
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
                    var newChunksLength = 2;
                    while (newChunksLength < chunkIdx + 1)
                        newChunksLength <<= 1;
                    newChunksLength--;
                    if (_mn == null)
                        _mn = new MaskInternal[newChunksLength];
                    else
                        Array.Resize(ref _mn, newChunksLength);
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

        string a;
        string b;
        string c;

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
                    a = Convert.ToString(m, 2).PadLeft(SizeOfPartInBits, '0');
                    b = Convert.ToString((1 << j), 2).PadLeft(SizeOfPartInBits, '0');
                    c = Convert.ToString((m & (1 << j)), 2).PadLeft(SizeOfPartInBits, '0');
                    
                    if ((m & (1 << j)) != 0)
                        return fromPosition + j;
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _m1 = 0;
            if (_mn != null)
            {
                for (int i = 0; i < _mn.Length; i++)
                    _mn[i] = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InclusivePass(in BitMask filter)
        {
            if (filter.Length > Length)
                return false;
            if ((filter._m1 & _m1) == 0)
                return false;
            if (filter._mn != null)
            {
                if (_mn == null)
                    return false;
                var chunksCount = filter.Length / SizeOfPartInBits;
                for (int i = 0; i < chunksCount - 1; i++)
                {
                    if ((filter._mn[i] & _mn[i]) == 0)
                        return false;
                }
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
                var chunksCount = filter.Length / SizeOfPartInBits;
                for (int i = 0; i < chunksCount && i < _mn.Length; i++)
                {
                    if ((filter._mn[i] & _mn[i]) != 0)
                        return false;
                }
            }

            return true;
        }

#if DEBUG
        public override string ToString()
        {
            var sb = new StringBuilder();
            if (_mn != null)
                for (int i = _mn.Length - 1; i > -1; i--)
                    sb.Append(Convert.ToString(_mn[i], 2).PadLeft(SizeOfPartInBits, '0'));
            sb.Append(Convert.ToString(_m1, 2).PadLeft(SizeOfPartInBits, '0'));
            sb.Append(". Length: " + Length);

            return sb.ToString();
        }
#endif
    }
}