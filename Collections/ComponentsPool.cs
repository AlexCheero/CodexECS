using CodexECS.Utility;
using System.Runtime.CompilerServices;
using System;
using System.Text;

namespace CodexECS
{
    public interface IComponentsPool
    {
        public int Length { get; }
        public bool Contains(int id);
        public void Remove(int id);
        public void Clear();
        public void Copy(in IComponentsPool other);
        public IComponentsPool Duplicate();

        public void CopyItem(int from, int to);

        public void AddReference(int id, object value);

        public string DebugString(int id, bool printFields);
        public Type GetComponentType();
    }

    class ComponentsPool<T> : IComponentsPool
    {
        //made public only for unrolling indexer for speeding up
        public int[] _sparse;
        private int[] _dense;
        public T[] _values;
#if DEBUG
        public int ValuesLength { get; private set; }
#else
        public int ValuesLength;
#endif

        public ref T this[int id]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _values[_sparse[id]];
        }

#if HEAVY_ECS_DEBUG
        private void CheckArrays()
        {
            for (int i = 0; i < ValuesLength; i++)
            {
                var outer = _dense[i];
                var inner = _sparse[outer];
                if (inner != i)
                    throw new EcsException("indices mismatch 2");
            }
        }
#endif

        private StringBuilder _debugStringBuilder;
        public string DebugString(int id, bool printFields)
        {
            _debugStringBuilder ??= new StringBuilder();
            _debugStringBuilder.Append(typeof(T).Name);
            if (printFields)
            {
                var props = _values[_sparse[id]].GetType().GetFields();
                if (props.Length > 0)
                    _debugStringBuilder.Append(':');
                else
                    _debugStringBuilder.Append(" {}");
                foreach (var p in props)
                {
                    var value = p.GetValue(_values[_sparse[id]]);
                    var valueString = value != null ? value.ToString() : "null";
                    _debugStringBuilder.Append("\n\t").Append(p.Name).Append(": ").Append(valueString).Append(", ");
                }
                if (props.Length > 0)
                    _debugStringBuilder.Remove(_debugStringBuilder.Length - 2, 2);//remove last comma
            }
            
            var result = _debugStringBuilder.ToString();
            _debugStringBuilder.Clear();
            return result;
        }

        public Type GetComponentType() => typeof(T);

        #region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ValuesLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int id) => id < _sparse.Length && _sparse[id] > -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int id)
        {
            var innerIndex = _sparse[id];
            _sparse[id] = -1;

#region Unrolled SimpleList.RemoveAt

            _values[innerIndex] = default;
            ValuesLength--;
            if (innerIndex < ValuesLength)
                _values[innerIndex] = _values[ValuesLength];

#endregion
            
            
            if (innerIndex < ValuesLength)
            {
                var lastId = _dense[ValuesLength];
                _sparse[lastId] = innerIndex;
                _dense[innerIndex] = _dense[ValuesLength];
                
                //TODO: ???
                //_sparse[id] = _dense[innerIndex];
                //_values[innerIndex] = _values[_values.Length];
            }

#if HEAVY_ECS_DEBUG
            CheckArrays();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER
            Array.Fill(_sparse, -1);
#else
            for (int i = 0; i < _sparse.Length; i++)
                _sparse[i] = -1;
#endif

#region Unrolled SimpleList.Clear

            ValuesLength = 0;

#endregion
            
            
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other)
        {
            var otherPool = (ComponentsPool<T>)other;

            if (_sparse.Length < otherPool._sparse.Length)
                Array.Resize(ref _sparse, otherPool._sparse.Length);
            else if (_sparse.Length > otherPool._sparse.Length)
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER
                Array.Fill(_sparse, -1, otherPool._sparse.Length, _sparse.Length - otherPool._sparse.Length);
#else
                for (int i = otherPool._sparse.Length; i < _sparse.Length; i++)
                    _sparse[i] = -1;
#endif
            }
            Array.Copy(otherPool._sparse, _sparse, otherPool._sparse.Length);

            if (_dense.Length < otherPool._dense.Length)
                Array.Resize(ref _dense, otherPool._dense.Length);
            Array.Copy(otherPool._dense, _dense, otherPool._dense.Length);

#region Unrolled SimpleList.Copy

            ValuesLength = otherPool.ValuesLength;
            if (_values.Length < ValuesLength)
                Array.Resize(ref _values, otherPool._values.Length);
            Array.Copy(otherPool._values, _values, ValuesLength);

#endregion
            
            

#if HEAVY_ECS_DEBUG
            CheckArrays();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool Duplicate()
        {
            var newPool = new ComponentsPool<T>(Length);
            newPool.Copy(this);
            return newPool;
        }

        public void CopyItem(int from, int to)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!Contains(from))
                throw new EcsException("trying to copy non existent component");
#endif
            Add(to, _values[_sparse[from]]);
        }
        #endregion

        public ComponentsPool(int initialCapacity = 2)
        {
            _sparse = new int[initialCapacity];
            _dense = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _values = new T[initialCapacity];
        }

        //CODEX_TODO: excess call, rewrite
        public void AddReference(int id, object value)
        {
#if DEBUG && !ECS_PERF_TEST
            if (value is ValueType)
                throw new EcsException("trying to add object of value type as reference");
#endif
            Add(id, (T)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(int id, T value)
        {
            if (id >= _sparse.Length)
            {
                var oldLength = _sparse.Length;
                const int maxResizeDelta = 256;
                Utils.ResizeArray(id, ref _sparse, maxResizeDelta);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_sparse[id] > -1)
                throw new EcsException(typeof(T) + " sparse set already have element at this index");
#endif

            _sparse[id] = ValuesLength;
            
#region Unrolled SimpleList.Add

            if (ValuesLength >= _values.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(ValuesLength, ref _values, maxResizeDelta);
            }
            _values[ValuesLength] = value;
            ValuesLength++;

#endregion
            
            
            if (_dense.Length < _values.Length)
                Array.Resize(ref _dense, _values.Length);
            _dense[_sparse[id]] = id;

#if HEAVY_ECS_DEBUG
            CheckArrays();
#endif

            return ref _values[_sparse[id]];
        }
    }

    class TagsPool<T> : IComponentsPool
    {
        private BitMask _tags;
        
        public string DebugString(int id, bool printFields) => typeof(T).Name;

        public Type GetComponentType() => typeof(T);

        #region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _tags.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int i) => _tags.Check(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int id) => _tags.Unset(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => _tags.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other) => _tags.Copy(((TagsPool<T>)other)._tags);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool Duplicate()
        {
            var newPool = new TagsPool<T>(Length);
            newPool.Copy(this);
            return newPool;
        }

        public void CopyItem(int from, int to)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!Contains(from))
                throw new EcsException("trying to copy non existent component");
#endif
            Add(to);
        }
        #endregion

        public TagsPool(int initialCapacity = 0)
        {
            _tags = new BitMask();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int id) => _tags.Set(id);

        public void AddReference(int id, object value) =>
            throw new EcsException("trying to call AddReference for TagsPool");
    }
}
