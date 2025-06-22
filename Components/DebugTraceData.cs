using CodexECS;
using System;
using System.Collections.Generic;

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
public struct DebugTraceData
{
    public enum EMethodType
    {
        Add,
        Remove
    }

    public struct Data
    {
        public string memberName;
        public string filePath;
        public int lineNumber;
    }

    public Dictionary<Type, Data> added;
    public Dictionary<Type, Data> removed;

    public static DebugTraceData Default => new()
    {
        added = new(),
        removed = new()
    };
    public static void Init(ref DebugTraceData instance)
    {
        instance.added ??= new();
        instance.removed ??= new();
    }

    public static void Cleanup(ref DebugTraceData instance)
    {
        instance.added?.Clear();
        instance.removed?.Clear();
    }
}
#endif