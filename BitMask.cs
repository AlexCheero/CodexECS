using System;
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
        private int _length;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        public BitMask(MaskInternal m1 = 0, MaskInternal[] mn = null)
        {
            _m1 = m1;
            _mn = mn;
            _length = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int i)
        {
            int chunkIdx = i / SizeOfPartInBits;
            ref var m = ref _m1;
            if (chunkIdx > 0)
            {
                chunkIdx--;
                //resize if needed
                if (_mn == null || _mn.Length <= chunkIdx)
                {
                    var newLength = 2;
                    while (newLength < chunkIdx + 1)
                        newLength <<= 1;
                    newLength--;
                    if (_mn == null)
                        _mn = new MaskInternal[newLength];
                    else
                        Array.Resize(ref _mn, newLength);
                }
                m = ref _mn[chunkIdx];
            }

            int position = i % SizeOfPartInBits;
            MaskInternal shift = 1;
            m |= (MaskInternal)(shift << position);

            //update length
            i++;
            if (_length < i)
                _length = i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(int i)
        {
            int chunkIdx = i / SizeOfPartInBits;
            if (chunkIdx > 0 && (_mn == null || _mn.Length <= chunkIdx - 1))
                return;

            ref var m = ref _m1;
            if (chunkIdx > 0)
                m = ref _mn[chunkIdx - 1];

            int position = i % SizeOfPartInBits;
            MaskInternal shift = 1;
            m &= (MaskInternal)~(shift << position);

            //update length
            if (chunkIdx == (_length - 1) / SizeOfPartInBits)
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
                _length = j * SizeOfPartInBits + msb;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Check(int i)
        {
            int chunkIdx = i / SizeOfPartInBits;
            if (chunkIdx > 0 && (_mn == null || _mn.Length <= chunkIdx - 1))
                return false;

            var m = _m1;
            if (chunkIdx > 0)
                m = _mn[chunkIdx - 1];
            int position = i % SizeOfPartInBits;
            return (m & (1 << position)) != 0;
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
                for (int i = 0; i < filter._mn.Length && i < _mn.Length; i++)
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
