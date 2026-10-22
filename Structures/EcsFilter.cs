using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace CodexECS
{
    //masks moved out of the filter to be used as a key
    public struct FilterMasks
    {
        public class EqualityComparer : IEqualityComparer<FilterMasks>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(FilterMasks x, FilterMasks y) => x.FilterMasksEquals(y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode(FilterMasks obj) => obj.GetMasksHash();
        }

        public static readonly EqualityComparer MasksComparer;
        static FilterMasks() => MasksComparer = new();

        public BitMask Includes;
        public BitMask Excludes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool FilterMasksEquals(FilterMasks other) =>
            Includes.MasksEquals(other.Includes) && Excludes.MasksEquals(other.Excludes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMasksHash() => Includes.GetMaskHash() * 23 + Excludes.GetMaskHash();
    }

    // Uncached scan state: chunk summaries pick candidate pages, page words are intersected per traversal.
    // Lives in a pooled object, so loops keep only a reference and the current id.
#if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
    internal abstract class FilterScan
    {
        // The current run ends before End (an absolute id); Word holds the page's remaining matches.
        // End is int.MinValue once the scan is exhausted, so "id < End" is the only loop test.
        internal int End, PageBase;
        internal ulong Word;
        private protected int Segment;
        private protected EcsFilter Filter;
        private ComponentStorage[] _includes, _excludes;
        private int _chunk, _chunks;
        private ulong _pages;
        // A scan may start inside a page (round-robin views): bits below are skipped once.
        private int _startPage;
        private ulong _startPageMask;
        private protected bool Active;
        internal EcsWorld World => Filter.World;

        private protected void Begin(EcsFilter filter, int startId)
        {
            Filter = filter;
            _includes = filter.Includes;
            _excludes = filter.Excludes;
            _chunks = (filter.World.PageCount + 63) >> 6;
            int startPage = startId >> Layout.PageShift;
            _chunk = (startPage >> 6) - 1;
            _pages = 0;
            _startPage = startPage;
            _startPageMask = ~0UL << (startId & 63);
            End = PageBase = 0;
            Word = 0;
            Segment = -1;
            if (startPage > 0)
                SkipToStartChunk();
        }

        private void SkipToStartChunk()
        {
            // The first chunk is entered with pages below the start page removed.
            if (++_chunk >= _chunks)
            {
                _pages = 0;
                return;
            }
            _pages = ChunkPages(_chunk) & (~0UL << (_startPage & 63));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong ChunkPages(int chunk)
        {
            var includes = _includes;
            ulong pages = includes[0].NonEmpty[chunk];
            for (int i = 1; i < includes.Length; i++)
                pages &= includes[i].NonEmpty[chunk];
            return pages;
        }

        private protected void SkipRemainingChunks()
        {
            _chunk = _chunks;
            _pages = 0;
        }

        private protected void Finish()
        {
            Active = false;
            _chunk = _chunks;
            _pages = 0;
            Word = 0;
            End = int.MinValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Entity EntityAt(int id) => EntityExtension.Make(id, Filter.World.Versions[id]);

        // First id of the next run; sets End. Runs inside the current page avoid any call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int NextRun()
        {
            ulong word = Word;
            return word != 0 ? TakeRun(word) : NextPage();
        }

        // Finds the next matching page and takes its first run (typed scans also bind payload segments).
        private protected abstract int NextPage();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TakeRun(ulong word)
        {
            // Lowest run of ones: adding its lowest bit carries to the bit just past the run.
            ulong low = word & (0UL - word);
            ulong next = word + low;
            Word = word & next;
            End = PageBase + Bits.TrailingZeros(next);
            return PageBase + Bits.TrailingZeros(low);
        }

        // False when exhausted. Otherwise PageBase is set and the first run taken.
        // Words and summaries are read afresh for every page, so storage growth during the loop is safe.
        private protected bool FindPage(out int start)
        {
            while (true)
            {
                while (_pages == 0)
                {
                    if (++_chunk >= _chunks)
                    {
                        End = int.MinValue;
                        start = 0;
                        return false;
                    }
                    _pages = ChunkPages(_chunk);
                }
                int page = (_chunk << 6) | Bits.TrailingZeros(_pages);
                _pages &= _pages - 1;
                var includes = _includes;
                ulong word = includes[0].Words[page];
                for (int i = 1; i < includes.Length; i++)
                    word &= includes[i].Words[page];
                var excludes = _excludes;
                for (int i = 0; i < excludes.Length; i++)
                    word &= ~excludes[i].Words[page];
                if (page == _startPage)
                    word &= _startPageMask;
                if (word == 0)
                    continue;
                PageBase = page << Layout.PageShift;
                start = TakeRun(word);
                return true;
            }
        }
    }

    // The entities owning every include and no exclude. Shared per pair of masks (EcsWorld.RegisterFilter).
    // Nothing is cached: each loop intersects the current membership words.
#if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
    public sealed partial class EcsFilter
    {
        public readonly EcsWorld World;
        // Never empty: the world's alive set stands in when there is no include.
        internal readonly ComponentStorage[] Includes;
        internal readonly ComponentStorage[] Excludes;
        internal readonly BitMask IncludeMask;
        private Iterator _free;
        private Dictionary<Type, object> _views;

        internal EcsFilter(EcsWorld world, in BitMask includes, in BitMask excludes)
        {
            World = world;
            IncludeMask = includes.Duplicate();
            var included = new List<ComponentStorage>();
            foreach (var id in includes)
                included.Add(world.GetStorage(id));
            if (included.Count == 0)
                included.Add(world.Alive);
            Includes = included.ToArray();
            var excluded = new List<ComponentStorage>();
            foreach (var id in excludes)
                excluded.Add(world.GetStorage(id));
            Excludes = excluded.ToArray();
        }

        // Locks the world for the loop: Delete is deferred until it ends.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new Enumerator(Rent(0));

        private Iterator Rent(int startId)
        {
            var iterator = _free ?? new Iterator();
            _free = iterator.NextFree;
            iterator.Begin(this, startId);
            return iterator;
        }

        // Scans until the first match; no lock is taken.
        public bool IsEmpty => First() < 0;

        // The lowest matching id, or -1.
        public EntityType First()
        {
            var iterator = Rent(0);
            int id = iterator.NextRun();
            bool found = id < iterator.End;
            iterator.Release(unlock: false);
            return found ? id : -1;
        }

        public bool TryFirst(out EntityType id)
        {
            id = First();
            return id >= 0;
        }

        // The lowest matching id at or above startId, or -1: resumes a scan where a previous tick stopped.
        public EntityType FirstFrom(EntityType startId)
        {
            if (startId <= 0)
                return First();
            if (startId >= World.SlotCapacity)
                return -1;
            var iterator = Rent(startId);
            int id = iterator.NextRun();
            bool found = id < iterator.End;
            iterator.Release(unlock: false);
            return found ? id : -1;
        }

        // Matching entities from startId to the end, in slot order: foreach (var id in filter.From(start)).
        public FromView From(EntityType startId) => new FromView(this, startId);

        public readonly struct FromView
        {
            private readonly EcsFilter _filter;
            private readonly EntityType _startId;

            internal FromView(EcsFilter filter, EntityType startId)
            {
                _filter = filter;
                _startId = startId;
            }

            public Enumerator GetEnumerator() =>
                new Enumerator(_filter.Rent(_startId > 0 && _startId < _filter.World.SlotCapacity ? _startId : 0),
                    exhausted: _startId >= _filter.World.SlotCapacity);
        }

        // Exact count by scanning every candidate page: O(pages). Prefer IsEmpty when only emptiness matters.
        public int EntitiesCount
        {
            get
            {
                var includes = Includes;
                var excludes = Excludes;
                int chunks = (World.PageCount + 63) >> 6, count = 0;
                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    ulong pages = includes[0].NonEmpty[chunk];
                    for (int i = 1; i < includes.Length; i++)
                        pages &= includes[i].NonEmpty[chunk];
                    while (pages != 0)
                    {
                        int page = (chunk << 6) | Bits.TrailingZeros(pages);
                        pages &= pages - 1;
                        ulong word = includes[0].Words[page];
                        for (int i = 1; i < includes.Length; i++)
                            word &= includes[i].Words[page];
                        for (int i = 0; i < excludes.Length; i++)
                            word &= ~excludes[i].Words[page];
                        count += Bits.PopCount(word);
                    }
                }
                return count;
            }
        }

        // Kept for loops written against the sparse-set filter; the world lock defers Delete.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lock() => World.Lock();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unlock() => World.Unlock();

        // Up to batch entities per loop, resuming after the last entity visited by the previous loop.
        public DistributedView GetDistributedView(int batch) => new(this, batch);

        // A typed view over this filter; T must be one of its data includes. Cached per type list.
        internal TView GetView<TView>(Func<EcsFilter, TView> create) where TView : class
        {
            _views ??= new Dictionary<Type, object>();
            if (!_views.TryGetValue(typeof(TView), out var view))
            {
                view = create(this);
                _views.Add(typeof(TView), view);
            }
            return (TView)view;
        }

#if DEBUG
        internal void CheckRowType<T>()
        {
            if (ComponentMeta<T>.IsTag)
                throw new EcsException($"{typeof(T).Name} is a tag: rows only expose data components");
            if (!IncludeMask.Check(ComponentMeta<T>.Id))
                throw new EcsException($"{typeof(T).Name} is not an include of this filter");
        }
#endif

#if ENABLE_IL2CPP
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
        internal sealed class Iterator : FilterScan
        {
            internal Iterator NextFree;

            internal new void Begin(EcsFilter filter, int startId)
            {
                NextFree = null;
                Active = true;
                base.Begin(filter, startId);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private protected override int NextPage() => FindPage(out int start) ? start : 0;

            internal void Lock() => Filter.World.Lock();

            internal void Exhaust()
            {
                Word = 0;
                End = int.MinValue;
                SkipRemainingChunks();
            }

            internal void Release(bool unlock)
            {
                if (!Active)
                    return;
                var filter = Filter;
                Finish();
                NextFree = filter._free;
                filter._free = this;
                if (unlock)
                    filter.World.Unlock();
            }
        }

        public struct Enumerator : IDisposable
        {
            private readonly Iterator _iterator;
            private EntityType _id;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(Iterator iterator)
            {
                iterator.Lock();
                _iterator = iterator;
                _id = -1;
            }

            // An exhausted enumerator visits nothing (a From view starting past the last slot).
            internal Enumerator(Iterator iterator, bool exhausted) : this(iterator)
            {
                if (exhausted)
                    _iterator.Exhaust();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                var iterator = _iterator;
                int id = _id + 1;
                if (id >= iterator.End)
                    id = iterator.NextRun();
                _id = id;
                return id < iterator.End;
            }

            public EntityType Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _id;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose() => _iterator.Release(unlock: true);
        }

        // Round-robin over the filter: each loop visits at most batch entities, starting after the last
        // entity the previous loop visited and wrapping to the lowest id once.
        public sealed class DistributedView
        {
            private readonly EcsFilter _filter;
            private int _maxEntitiesPerTick;
            private int _nextStart;

            internal DistributedView(EcsFilter filter, int maxEntitiesPerTick)
            {
                _filter = filter;
                _maxEntitiesPerTick = maxEntitiesPerTick;
            }

            // May change between loops, for batches that follow the filter's size.
            public int MaxEntitiesPerTick
            {
                get => _maxEntitiesPerTick;
                set => _maxEntitiesPerTick = value;
            }

            public DistributedEnumerator GetEnumerator()
            {
                var start = _nextStart < _filter.World.SlotCapacity ? _nextStart : 0;
                return new DistributedEnumerator(this, _filter.Rent(start), start);
            }

            public struct DistributedEnumerator : IDisposable
            {
                private readonly DistributedView _view;
                private Iterator _iterator;
                private readonly int _start;
                private bool _wrapped;
                private int _left;
                private EntityType _id;

                internal DistributedEnumerator(DistributedView view, Iterator iterator, int start)
                {
                    iterator.Lock();
                    _view = view;
                    _iterator = iterator;
                    _start = start;
                    _wrapped = start == 0;
                    _left = view._maxEntitiesPerTick;
                    _id = -1;
                }

                public bool MoveNext()
                {
                    if (_left <= 0)
                        return false;
                    while (true)
                    {
                        int id = _id + 1;
                        if (id >= _iterator.End)
                            id = _iterator.NextRun();
                        if (id < _iterator.End && (!_wrapped || id < _start || _start == 0))
                        {
                            _id = id;
                            _left--;
                            return true;
                        }
                        if (_wrapped || _start == 0)
                            return false;
                        // Second pass from the lowest id up to where this loop started.
                        _wrapped = true;
                        _iterator.Release(unlock: false);
                        _iterator = _view._filter.Rent(0);
                        _id = -1;
                    }
                }

                public EntityType Current
                {
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    get => _id;
                }

                public void Dispose()
                {
                    _view._nextStart = _id + 1;
                    _iterator.Release(unlock: true);
                }
            }
        }
    }
}
