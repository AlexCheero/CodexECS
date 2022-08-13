using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if DEBUG
using System.Text;
#endif

namespace ECS
{
    interface IComponentsPool
    {
        public int Length { get; }
        public bool Contains(int id);
        public void Remove(int id);
        public void Clear();
        public void Copy(in IComponentsPool other);
        public IComponentsPool Duplicate();

        public void Serialize(byte[] outBytes, ref int startIndex);
        public void Deserialize(byte[] bytes, ref int startIndex);
        public int ByteLength();

#if DEBUG
        public string DebugString(int id);
#endif
    }

    class ComponentsPool<T> : IComponentsPool
    {
        //made public only for unrolling indexer for speeding up
        public int[] _sparse;
        private int[] _dense;
        public SimpleVector<T> _values;

#if DEBUG
        private void CheckArrays()
        {
            for (int i = 0; i < _values.Length; i++)
            {
                var outer = _dense[i];
                var inner = _sparse[outer];
                if (inner != i)
                    throw new EcsException("indices mismatch 2");
            }
        }
#endif

        #region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _values.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int id) => id < _sparse.Length && _sparse[id] > -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int id)
        {
            var innerIndex = _sparse[id];
            _sparse[id] = -1;
            _values.Remove(innerIndex);
            if (innerIndex < _values.Length)
            {
                var lastId = _dense[_values.Length];
                _sparse[lastId] = innerIndex;
                _dense[innerIndex] = _dense[_values.Length];
                //_sparse[id] = _dense[innerIndex];
                //_values[innerIndex] = _values[_values.Length];
            }

#if DEBUG
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

            _values.Clear();
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

            _values.Copy(otherPool._values);

#if DEBUG
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

        public void Serialize(byte[] outBytes, ref int startIndex)
        {
            //serialize only struct components
            if (!typeof(T).IsValueType)
                return;

            BinarySerializer.SerializeInt(_sparse.Length, outBytes, ref startIndex);
            BinarySerializer.SerializeIntegerArray(_sparse, outBytes, ref startIndex);

            BinarySerializer.SerializeInt(_dense.Length, outBytes, ref startIndex);
            BinarySerializer.SerializeIntegerArray(_dense, outBytes, ref startIndex);

            BinarySerializer.SerializeInt(_values.Length, outBytes, ref startIndex);
            BinarySerializer.SerializeInt(_values._elements.Length, outBytes, ref startIndex);
            for (int i = 0; i < _values.Length; i++)
                BinarySerializer.SerializeStruct(_values[i], outBytes, ref startIndex);
        }

        public void Deserialize(byte[] bytes, ref int startIndex)
        {
            //deserialize only struct components
            if (!typeof(T).IsValueType)
                return;

            var sparseLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _sparse = BinarySerializer.DeserializeIntegerArray(bytes, ref startIndex, sparseLength);

            var denseLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _dense = BinarySerializer.DeserializeIntegerArray(bytes, ref startIndex, denseLength);

            _values._end = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            var sizeOfInstance = Marshal.SizeOf(default(T));
            var valuesElementsLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _values._elements = new T[valuesElementsLength];
            for (int i = 0; i < _values.Length; i++)
                _values[i] = BinarySerializer.DeserializeStruct<T>(bytes, ref startIndex, sizeOfInstance);
        }

        public int ByteLength()
        {
            if (typeof(T).IsValueType)
                return 8 + (_sparse.Length * 4) + (_dense.Length * 4) + (Marshal.SizeOf(default(T)) * _values.Length);
            else
                return 0; //serialize only struct components
        }
        #endregion

        private ComponentsPool()
        {
            _sparse = new int[EcsCacheSettings.PoolSize];
            _dense = new int[EcsCacheSettings.PoolSize];
            for (int i = 0; i < EcsCacheSettings.PoolSize; i++)
                _sparse[i] = -1;
            _values = new SimpleVector<T>(EcsCacheSettings.PoolSize);
        }

        //factory method for deserialization
        public static ComponentsPool<T> CreateUninitialized() => new ComponentsPool<T>();

        public ComponentsPool(int initialCapacity)
        {
            _sparse = new int[initialCapacity];
            _dense = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _values = new SimpleVector<T>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(int id, T value)
        {
            if (id >= _sparse.Length)
            {
                var oldLength = _sparse.Length;
                var newLength = oldLength > 0 ? oldLength * 2 : 2;
                while (id >= newLength)
                    newLength *= 2;
                Array.Resize(ref _sparse, newLength);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

#if DEBUG
            if (_sparse[id] > -1)
                throw new EcsException("sparse set already have element at this index");
#endif

            _sparse[id] = _values.Length;
            _values.Add(value);
            if (_dense.Length < _values._elements.Length)
                Array.Resize(ref _dense, _values._elements.Length);
            _dense[_sparse[id]] = id;

#if DEBUG
            CheckArrays();
#endif

            return ref _values[_sparse[id]];
        }

#if DEBUG
        public string DebugString(int id)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(typeof(T).ToString() + ". ");

            var props = _values[_sparse[id]].GetType().GetFields();
            foreach (var p in props)
                sb.Append(p.Name + ": " + p.GetValue(_values[_sparse[id]]) + ", ");
            sb.Remove(sb.Length - 2, 2);//remove last comma

            return sb.ToString();
        }
#endif
    }

    class TagsPool<T> : IComponentsPool
    {
        private BitMask _tags;

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

        public void Serialize(byte[] outBytes, ref int startIndex)
        {
            _tags.Serialize(outBytes, ref startIndex);
        }

        public void Deserialize(byte[] bytes, ref int startIndex)
        {
            _tags.Deserialize(bytes, ref startIndex);
        }

        public int ByteLength() => _tags.ByteLength;
        #endregion

        public TagsPool(int initialCapacity = 0)
        {
            _tags = new BitMask();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int id) => _tags.Set(id);

#if DEBUG
        public string DebugString(int id)
        {
            return typeof(T).ToString();
        }
#endif
    }
}
