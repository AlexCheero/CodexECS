using System.Runtime.CompilerServices;

namespace CodexECS
{
    /// <summary>
    /// Borrowed membership pages, valid until the owning phase ends. This does
    /// not lock the world or make concurrent structural changes safe.
    /// </summary>
    public readonly struct ComponentMembershipPages
    {
        private readonly ulong[] _words;
        private readonly ulong[] _nonEmpty;
        internal ComponentMembershipPages(ulong[] words, ulong[] nonEmpty)
        {
            _words = words;
            _nonEmpty = nonEmpty;
        }

        public ulong this[int page]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _words[page];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FirstSlot(int page) => page << Layout.PageShift;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NonEmptyPages(int chunk) => _nonEmpty[chunk];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ChunkCount(int pages) => (pages + 63) >> 6;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FirstPage(int chunk) => chunk << 6;

        /// <summary>Removes and returns the lowest set bit. The mask must be nonzero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TakeFirst(ref ulong mask)
        {
            int bit = Bits.TrailingZeros(mask);
            mask &= mask - 1;
            return bit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Count(ulong mask) => Bits.PopCount(mask);
    }

    /// <summary>
    /// Borrowed component directories. Payload arrays are shared with the world,
    /// not snapshots. A phase must own every slot it writes and exclude other
    /// writers until completion. Recapture directories after world growth.
    /// </summary>
    public readonly struct ComponentPayloadPages<T>
    {
        public readonly ComponentMembershipPages Membership;
        private readonly T[][] _segments;

        internal ComponentPayloadPages(ulong[] words, ulong[] nonEmpty, T[][] segments)
        {
            Membership = new ComponentMembershipPages(words, nonEmpty);
            _segments = segments;
        }

        /// <summary>
        /// The backing array containing this page. It may be null if the segment
        /// has never held this component; access only slots in Membership.
        /// Do not write outside the caller's owned page or retain the borrow.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] Segment(int page) => _segments[page >> (Layout.SegmentShift - Layout.PageShift)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Offset(int page) => (page << Layout.PageShift) & Layout.SegmentMask;
    }
}
