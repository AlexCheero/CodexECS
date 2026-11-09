using System;

namespace CodexECS
{
    // State capture and restore. Additive to the storage code: nothing here changes how entities are
    // allocated or how components are stored; it copies that state out and writes it back.
    public partial class EcsWorld
    {
        // Per page: slots that keep their identity through a restore (alive afterwards, same version as now).
        private ulong[] _restoreKeep = Array.Empty<ulong>();

        /// <summary>
        /// Copies the slot bookkeeping (alive set, versions, page and group allocation) and the listed
        /// component storages into <paramref name="state"/>. Storages of listed components that were
        /// never used are created, so a later restore has a target. Pending deferred deletes and
        /// reactions are flushed first. The world must not be locked.
        /// Listed components must be unmanaged (checked in DEBUG).
        /// </summary>
        public void CaptureState(WorldState state, ReadOnlySpan<int> componentIds)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (IsLocked)
                throw new EcsException("CaptureState: the world is locked");
            FlushReactives();

            int pages = _pagesUsed;
            state.Begin(pages, componentIds, this);

            Array.Copy(Alive.Words, state.AliveWordsArray, pages);
            state.AliveCount = Alive.Length;
            Array.Copy(Versions, state.VersionsArray, pages << Layout.PageShift);

            for (int p = 0; p < pages; p++)
            {
                ref PageInfo info = ref _pageInfo[p];
                state.Pages[p] = new WorldState.PageRecord
                {
                    Reserved = info.Reserved,
                    Group = info.Group,
                    Next = info.Next,
                    Previous = info.Previous,
                };
                int typesLength = info.Types == null ? 0 : info.Types.Length;
                if (typesLength > 0)
                {
                    ref ulong[] copy = ref state.PageTypes[p];
                    if (copy == null || copy.Length < typesLength)
                        copy = new ulong[typesLength];
                    Array.Copy(info.Types, copy, typesLength);
                }
                state.PageTypesLength[p] = typesLength;
            }

            StateArrays.EnsureLength(ref state.GroupHeads, _groupHeads.Length);
            Array.Copy(_groupHeads, state.GroupHeads, _groupHeads.Length);
            state.GroupHeadCount = _groupHeads.Length;
            state.EmptyPages = _emptyPages;
            state.Live = _live;
            state.PagesUsed = pages;

            for (int i = 0; i < state.ComponentCount; i++)
            {
                ComponentStorage storage = GetStorage(state.ComponentIdArray[i]);
                StorageState storageState = state.StorageStates[i];
                storage.CaptureMembership(storageState, pages);
                storage.CapturePayload(storageState, pages);
            }
            state.Captured = true;
        }

        /// <summary>
        /// Puts the world back into a captured state. Listed storages are restored exactly. Storages
        /// outside the state keep their components only on slots that are alive after the restore with
        /// an unchanged version; on dead or reused slots they are removed with Cleanup, without
        /// reactions. Pages handed out after the capture are emptied. Entity handles captured in the
        /// state stay valid; handles created after the capture become invalid.
        /// Pending deferred deletes and reactions are flushed first. The world must not be locked.
        /// Restoring into a different world than the captured one is allowed; only allocation grouping
        /// of later entities may differ.
        /// </summary>
        public void RestoreState(WorldState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (!state.Captured)
                throw new EcsException("RestoreState: the state was never captured");
            if (IsLocked)
                throw new EcsException("RestoreState: the world is locked");
            FlushReactives();

            int pages = state.PagesUsed;
            while (_pages < pages)
                Grow();
            int totalPages = Math.Max(_pagesUsed, pages);
            int totalSlots = totalPages << Layout.PageShift;

            // Listed storages must exist, also when restoring into a fresh world.
            for (int i = 0; i < state.ComponentCount; i++)
                GetStorage(state.ComponentIdArray[i]);

            // 1. Slots that keep their identity.
            StateArrays.EnsureLength(ref _restoreKeep, pages);
            for (int p = 0; p < pages; p++)
            {
                ulong alive = state.AliveWordsArray[p];
                ulong keep = 0;
                while (alive != 0)
                {
                    int bit = Bits.TrailingZeros(alive);
                    alive &= alive - 1;
                    int id = (p << Layout.PageShift) | bit;
                    if (Versions[id] == state.VersionsArray[id])
                        keep |= 1UL << bit;
                }
                _restoreKeep[p] = keep;
            }

            // 2. Storages: restore the listed ones, prune the others.
            ComponentStorage[] storages = Storages;
            for (int typeId = 0; typeId < storages.Length; typeId++)
            {
                ComponentStorage storage = storages[typeId];
                if (storage == null)
                    continue;
                int index = state.IndexOf(typeId);
                if (index < 0)
                {
                    storage.Prune(_restoreKeep, pages, totalPages);
                }
                else
                {
                    StorageState storageState = state.StorageStates[index];
                    storage.RestoreMembership(storageState.Words, pages, storageState.Count);
                    storage.RestorePayload(storageState, pages);
                }
            }

            // 3. Alive set and versions. Versions of slots beyond the captured pages stay as they are:
            //    they only ever grow, which keeps every handle to them stale.
            Alive.RestoreMembership(state.AliveWordsArray, pages, state.AliveCount);
            Array.Copy(state.VersionsArray, Versions, pages << Layout.PageShift);

            // 4. Page bookkeeping.
            for (int p = 0; p < pages; p++)
            {
                ref PageInfo info = ref _pageInfo[p];
                WorldState.PageRecord record = state.Pages[p];
                info.Reserved = record.Reserved;
                info.Group = record.Group;
                info.Next = record.Next;
                info.Previous = record.Previous;
                // "Types ever on this page" only grows: surviving components outside the state are still there.
                int typesLength = state.PageTypesLength[p];
                if (typesLength > 0)
                {
                    if (info.Types == null || info.Types.Length < typesLength)
                        Array.Resize(ref info.Types, typesLength);
                    ulong[] captured = state.PageTypes[p];
                    for (int w = 0; w < typesLength; w++)
                        info.Types[w] |= captured[w];
                }
            }
            for (int p = pages; p < totalPages; p++)
            {
                ref PageInfo info = ref _pageInfo[p];
                info.Reserved = 0;
                info.Group = -1;
                info.Next = -1;
                info.Previous = -1;
                if (info.Types != null)
                    Array.Clear(info.Types, 0, info.Types.Length);
            }

            // 5. Group lists and counters.
            if (_groupHeads.Length < state.GroupHeadCount)
                Array.Resize(ref _groupHeads, state.GroupHeadCount);
            Array.Copy(state.GroupHeads, _groupHeads, state.GroupHeadCount);
            for (int i = state.GroupHeadCount; i < _groupHeads.Length; i++)
                _groupHeads[i] = -1;
            _emptyPages = state.EmptyPages;
            _live = state.Live;
            _pagesUsed = pages;

            // 6. Per-slot component counts from what is actually stored now.
            Array.Clear(_componentCounts, 0, totalSlots);
            for (int typeId = 0; typeId < storages.Length; typeId++)
            {
                ComponentStorage storage = storages[typeId];
                if (storage == null)
                    continue;
                ulong[] words = storage.Words;
                int limit = Math.Min(totalPages, words.Length);
                for (int p = 0; p < limit; p++)
                {
                    ulong bits = words[p];
                    while (bits != 0)
                    {
                        int id = (p << Layout.PageShift) | Bits.TrailingZeros(bits);
                        bits &= bits - 1;
                        _componentCounts[id]++;
                    }
                }
            }
        }
    }
}
