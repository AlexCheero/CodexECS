using System;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    /// <summary>
    /// Internal ECS storage for the components after the canonical first <typeparamref name="T"/>.
    /// The first component always remains in the regular <typeparamref name="T"/> pool so that
    /// <see cref="EcsWorld.Get{T}(int)"/> keeps its normal meaning.
    /// </summary>
    internal struct MultipleComponents<T> : IComponent
    {
        internal SimpleList<T> components;

        private static void Init(ref MultipleComponents<T> instance) =>
            instance.components ??= new SimpleList<T>();

        private static void Cleanup(ref MultipleComponents<T> instance)
        {
            if (instance.components == null)
                return;

            for (var i = 0; i < instance.components.Length; i++)
                ComponentMeta<T>.Cleanup(ref instance.components[i]);
            instance.components.Clear();
        }

        private static void Copy(in MultipleComponents<T> source, ref MultipleComponents<T> destination)
        {
            if (ReferenceEquals(source.components, destination.components))
                destination.components = new SimpleList<T>(Math.Max(source.components?.Length ?? 0, 2));
            else
                destination.components ??= new SimpleList<T>(Math.Max(source.components?.Length ?? 0, 2));
            destination.components.Clear();
            if (source.components == null)
                return;

            for (var i = 0; i < source.components.Length; i++)
            {
                var copiedComponent = default(T);
                ComponentMeta<T>.Copy(in source.components[i], ref copiedComponent);
                destination.components.Add(copiedComponent);
            }
        }
    }

    /// <summary>
    /// A zero-allocation, ref-indexable view over every component of type <typeparamref name="T"/>
    /// on one entity. Index zero is the canonical component returned by <see cref="EcsWorld.Get{T}(int)"/>;
    /// later indices are backed by the internal additional-component storage.
    /// </summary>
    public readonly struct MultipleComponentCollection<T>
    {
        private readonly EcsWorld _world;
        private readonly int _eid;
        private readonly SimpleList<T> _additionalComponents;

        internal MultipleComponentCollection(EcsWorld world, int eid)
        {
            _world = world;
            _eid = eid;
            _additionalComponents = world.HaveMultipleStorage<T>(eid)
                ? world.Get<MultipleComponents<T>>(eid).components
                : null;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1 + (_additionalComponents?.Length ?? 0);
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Count;
        }

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                if (index == 0)
                    return ref _world.Get<T>(_eid);
                return ref _additionalComponents[index - 1];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(_world, _eid, _additionalComponents);

        public struct Enumerator
        {
            private readonly EcsWorld _world;
            private readonly int _eid;
            private readonly SimpleList<T> _additionalComponents;
            private int _index;

            internal Enumerator(EcsWorld world, int eid, SimpleList<T> additionalComponents)
            {
                _world = world;
                _eid = eid;
                _additionalComponents = additionalComponents;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _index++;
                return _index < 1 + (_additionalComponents?.Length ?? 0);
            }

            public ref T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    if (_index == 0)
                        return ref _world.Get<T>(_eid);
                    return ref _additionalComponents[_index - 1];
                }
            }
        }
    }

}
