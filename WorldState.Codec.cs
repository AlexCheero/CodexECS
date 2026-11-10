using System;
using System.Buffers.Binary;

namespace CodexECS
{
    // Byte form of a captured state, for saving to disk or sending to another process. The format is
    // little-endian and versioned; both sides must register the same component types (ids may
    // differ: the component list travels as type names resolved through ComponentMapping).
    public sealed partial class WorldState
    {
        private const uint CodecMagic = 0x53534345; // "ECSS"
        private const ushort CodecVersion = 1;

        /// <summary>Bytes <see cref="Serialize"/> will write for the current contents.</summary>
        public int SerializedSize
        {
            get
            {
                if (!Captured)
                    throw new EcsException("state was never captured");
                int size = 4 + 2 + 4 * 6;
                size += PagesUsed * 8;                       // alive words
                size += SlotCount * 4;                       // versions
                size += PagesUsed * (8 + 4 + 4 + 4 + 4);     // page records + types length
                for (int p = 0; p < PagesUsed; p++)
                    size += PageTypesLength[p] * 8;
                size += GroupHeadCount * 4;
                for (int i = 0; i < ComponentCount; i++)
                {
                    string name = ComponentMapping.GetTypeForId(ComponentIdArray[i]).AssemblyQualifiedName;
                    size += 2 + System.Text.Encoding.UTF8.GetByteCount(name);
                    StorageState storage = StorageStates[i];
                    size += 4 + 4 + PagesUsed * 8;           // element size, count, words
                    int pageBytes = storage.ElementSize << Layout.PageShift;
                    for (int p = 0; p < PagesUsed; p++)
                    {
                        if (storage.Words[p] != 0)
                            size += pageBytes;
                    }
                }
                return size;
            }
        }

        /// <summary>Writes the state; returns the byte count. The destination must hold <see cref="SerializedSize"/> bytes.</summary>
        public int Serialize(Span<byte> destination)
        {
            if (!Captured)
                throw new EcsException("state was never captured");
            int offset = 0;
            WriteU32(destination, ref offset, CodecMagic);
            WriteU16(destination, ref offset, CodecVersion);
            WriteI32(destination, ref offset, PagesUsed);
            WriteI32(destination, ref offset, Live);
            WriteI32(destination, ref offset, AliveCount);
            WriteI32(destination, ref offset, EmptyPages);
            WriteI32(destination, ref offset, GroupHeadCount);
            WriteI32(destination, ref offset, ComponentCount);

            for (int p = 0; p < PagesUsed; p++)
                WriteU64(destination, ref offset, AliveWordsArray[p]);
            for (int s = 0; s < SlotCount; s++)
                WriteU32(destination, ref offset, VersionsArray[s]);
            for (int p = 0; p < PagesUsed; p++)
            {
                WriteU64(destination, ref offset, Pages[p].Reserved);
                WriteI32(destination, ref offset, Pages[p].Group);
                WriteI32(destination, ref offset, Pages[p].Next);
                WriteI32(destination, ref offset, Pages[p].Previous);
                WriteI32(destination, ref offset, PageTypesLength[p]);
                for (int w = 0; w < PageTypesLength[p]; w++)
                    WriteU64(destination, ref offset, PageTypes[p][w]);
            }
            for (int g = 0; g < GroupHeadCount; g++)
                WriteI32(destination, ref offset, GroupHeads[g]);

            for (int i = 0; i < ComponentCount; i++)
            {
                string name = ComponentMapping.GetTypeForId(ComponentIdArray[i]).AssemblyQualifiedName;
                int nameBytes = System.Text.Encoding.UTF8.GetBytes(name, destination.Slice(offset + 2));
                WriteU16(destination, ref offset, (ushort)nameBytes);
                offset += nameBytes;
                StorageState storage = StorageStates[i];
                WriteI32(destination, ref offset, storage.ElementSize);
                WriteI32(destination, ref offset, storage.Count);
                for (int p = 0; p < PagesUsed; p++)
                    WriteU64(destination, ref offset, storage.Words[p]);
                int pageBytes = storage.ElementSize << Layout.PageShift;
                for (int p = 0; p < PagesUsed; p++)
                {
                    if (storage.Words[p] == 0)
                        continue;
                    storage.CopyPageOut(p, destination.Slice(offset, pageBytes));
                    offset += pageBytes;
                }
            }
            return offset;
        }

        /// <summary>
        /// Fills this state from bytes written by <see cref="Serialize"/>. Component types are resolved
        /// by name and must be registered here; <paramref name="world"/> supplies the storages the
        /// typed payload buffers are created from. False on malformed input or unknown types.
        /// </summary>
        public bool Deserialize(ReadOnlySpan<byte> source, EcsWorld world)
        {
            Captured = false;
            int offset = 0;
            try
            {
                if (ReadU32(source, ref offset) != CodecMagic || ReadU16(source, ref offset) != CodecVersion)
                    return false;
                int pages = ReadI32(source, ref offset);
                int live = ReadI32(source, ref offset);
                int aliveCount = ReadI32(source, ref offset);
                int emptyPages = ReadI32(source, ref offset);
                int groupHeadCount = ReadI32(source, ref offset);
                int componentCount = ReadI32(source, ref offset);
                if (pages < 0 || componentCount < 0 || groupHeadCount < 0)
                    return false;

                // Resolve the component list first so Begin can size everything.
                int headerEnd = offset;
                int tempOffset = offset;
                tempOffset += pages * 8 + (pages << Layout.PageShift) * 4;
                for (int p = 0; p < pages; p++)
                {
                    tempOffset += 8 + 4 + 4 + 4;
                    int typesLength = ReadI32(source, ref tempOffset);
                    tempOffset += typesLength * 8;
                }
                tempOffset += groupHeadCount * 4;
                var ids = new int[componentCount];
                for (int i = 0; i < componentCount; i++)
                {
                    int nameLength = ReadU16(source, ref tempOffset);
                    string name = System.Text.Encoding.UTF8.GetString(source.Slice(tempOffset, nameLength).ToArray());
                    tempOffset += nameLength;
                    Type type = Type.GetType(name, false);
                    if (type == null)
                        return false;
                    ids[i] = ComponentMapping.EnsureTypeRegistered(type);
                    int elementSize = ReadI32(source, ref tempOffset);
                    tempOffset += 4; // count
                    int occupied = 0;
                    for (int p = 0; p < pages; p++)
                    {
                        if (ReadU64(source, ref tempOffset) != 0)
                            occupied++;
                    }
                    tempOffset += occupied * (elementSize << Layout.PageShift);
                }
                if (tempOffset > source.Length)
                    return false;

                Begin(pages, ids, world);
                offset = headerEnd;
                for (int p = 0; p < pages; p++)
                    AliveWordsArray[p] = ReadU64(source, ref offset);
                for (int s = 0; s < SlotCount; s++)
                    VersionsArray[s] = ReadU32(source, ref offset);
                for (int p = 0; p < pages; p++)
                {
                    Pages[p].Reserved = ReadU64(source, ref offset);
                    Pages[p].Group = ReadI32(source, ref offset);
                    Pages[p].Next = ReadI32(source, ref offset);
                    Pages[p].Previous = ReadI32(source, ref offset);
                    int typesLength = ReadI32(source, ref offset);
                    PageTypesLength[p] = typesLength;
                    if (typesLength > 0)
                    {
                        if (PageTypes[p] == null || PageTypes[p].Length < typesLength)
                            PageTypes[p] = new ulong[typesLength];
                        for (int w = 0; w < typesLength; w++)
                            PageTypes[p][w] = ReadU64(source, ref offset);
                    }
                }
                StateArrays.EnsureLength(ref GroupHeads, groupHeadCount);
                for (int g = 0; g < groupHeadCount; g++)
                    GroupHeads[g] = ReadI32(source, ref offset);
                GroupHeadCount = groupHeadCount;

                for (int i = 0; i < componentCount; i++)
                {
                    int nameLength = ReadU16(source, ref offset);
                    offset += nameLength;
                    int elementSize = ReadI32(source, ref offset);
                    StorageState storage = StorageStates[i];
                    if (elementSize != storage.ElementSize)
                        return false; // the type changed shape between the two sides
                    storage.Count = ReadI32(source, ref offset);
                    for (int p = 0; p < pages; p++)
                        storage.Words[p] = ReadU64(source, ref offset);
                    int pageBytes = elementSize << Layout.PageShift;
                    for (int p = 0; p < pages; p++)
                    {
                        if (storage.Words[p] == 0)
                            continue;
                        storage.CopyPageIn(p, source.Slice(offset, pageBytes));
                        offset += pageBytes;
                    }
                }
                Live = live;
                AliveCount = aliveCount;
                EmptyPages = emptyPages;
                PagesUsed = pages;
                Captured = true;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static void WriteU16(Span<byte> d, ref int o, ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(d.Slice(o), v); o += 2; }
        private static void WriteU32(Span<byte> d, ref int o, uint v) { BinaryPrimitives.WriteUInt32LittleEndian(d.Slice(o), v); o += 4; }
        private static void WriteI32(Span<byte> d, ref int o, int v) { BinaryPrimitives.WriteInt32LittleEndian(d.Slice(o), v); o += 4; }
        private static void WriteU64(Span<byte> d, ref int o, ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(d.Slice(o), v); o += 8; }
        private static ushort ReadU16(ReadOnlySpan<byte> s, ref int o) { ushort v = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(o)); o += 2; return v; }
        private static uint ReadU32(ReadOnlySpan<byte> s, ref int o) { uint v = BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(o)); o += 4; return v; }
        private static int ReadI32(ReadOnlySpan<byte> s, ref int o) { int v = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(o)); o += 4; return v; }
        private static ulong ReadU64(ReadOnlySpan<byte> s, ref int o) { ulong v = BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(o)); o += 8; return v; }
    }
}
