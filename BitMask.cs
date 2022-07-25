using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace ECS
{
    using MaskInternal = UInt32;

    public struct BitMask
    {
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

        public BitMask(MaskInternal m1 = 0, MaskInternal[] mn = null)
        {
            _m1 = m1;
            _mn = mn;
            Length = 0;
        }

        public BitMask(params int[] positions)
        {
            _m1 = 0;
            _mn = null;
            Length = 0;
            Set(positions);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetChunksLength(int length) => (int)Math.Ceiling((float)length / SizeOfPartInBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in BitMask other)
        {
            _m1 = other._m1;
            Length = other.Length;
            var chunksLength = GetChunksLength(Length);
            if (chunksLength > 1)
            {
                if (_mn == null || _mn.Length < Length)
                {
                    var newChunksLength = 2;
                    while (newChunksLength < chunksLength)
                        newChunksLength <<= 1;
                    _mn = new MaskInternal[newChunksLength];
                }

                for (int i = 0; i < chunksLength; i++)
                    _mn[i] = other._mn[i];
            }
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

        /* usage:
         * var nextSetBit = mask.GetNextSetBit(0);
         * while (nextSetBit != -1)
         * {
         *     your code here
         *     nextSetBit = mask.GetNextSetBit(nextSetBit + 1);
         * }
         */
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
        private bool InclusivePass_Internal(MaskInternal value, MaskInternal filter) => (filter & (value ^ filter)) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InclusivePass(in BitMask filter)
        {
            if (filter.Length > Length)
                return false;
            if (!InclusivePass_Internal(_m1, filter._m1))
                return false;

            var chunksCount = GetChunksLength(filter.Length);
            for (int i = 0; i < chunksCount - 1; i++)
            {
                var filterChunk = filter._mn[i];
                if (filterChunk == 0)
                    continue;

                if (!InclusivePass_Internal(_mn[i], filterChunk))
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
                var chunksCount = GetChunksLength(filter.Length);
                for (int i = 0; i < chunksCount && i < _mn.Length; i++)
                {
                    if ((filter._mn[i] & _mn[i]) != 0)
                        return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(in BitMask other)
        {
            if (Length != other.Length)
                return false;

            if (_m1 != other._m1)
                return false;

            if (_mn != null)
            {
                int length = GetChunksLength(Length) - 1;
                for (int i = 0; i < length; i++)
                {
                    if (_mn[i] != other._mn[i])
                        return false;
                }
            }

            return true;
        }

        public void Serialize(byte[] outBytes, ref int startIndex)
        {
            for (int i = 0; i < sizeof(int); i++)
                outBytes[startIndex++] = (byte)(Length >> (8 * i));

            for (int i = 0; i < sizeof(MaskInternal); i++)
                outBytes[startIndex++] = (byte)(_m1 >> (8 * i));

            if (_mn == null || _mn.Length == 0)
            {
                BinarySerializer.SerializeInt(0, outBytes, ref startIndex);
            }
            if (_mn != null && _mn.Length > 0)
            {
                BinarySerializer.SerializeInt(_mn.Length, outBytes, ref startIndex);

                for (int i = 0; i < _mn.Length; i++)
                {
                    for (int j = 0; j < sizeof(MaskInternal); j++)
                        outBytes[startIndex++] = (byte)(_mn[i] >> (8 * j));
                }
            }
        }

        public void Deserialize(byte[] bytes, ref int startIndex)
        {
            Length = bytes[startIndex++];
            for (int i = 1; i < sizeof(int); i++)
                Length |= bytes[startIndex++] << 8 * i;

            _m1 = bytes[startIndex++];
            for (int i = 1; i < sizeof(MaskInternal); i++)
                _m1 |= (MaskInternal)bytes[startIndex++] << 8 * i;

            int mnLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            if (mnLength > 0)
            {
                _mn = new MaskInternal[mnLength];
                for (int i = 0; i < _mn.Length; i++)
                {
                    for (int j = 0; j < sizeof(MaskInternal); j++)
                        _mn[i] |= (MaskInternal)bytes[startIndex++] << 8 * i;
                }
            }
        }

        public int ByteLength => Length * 8;

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

        public string ChunkToString(MaskInternal chunk) => Convert.ToString(chunk, 2).PadLeft(SizeOfPartInBits, '0');

        public void SetBits(int[] bits)
        {
            int j = 0;
            for (int i = bits.Length - 1; i >= 0; i--, j++)
                if (bits[i] != 0)
                    Set(j);
        }
#endif
    }
}
