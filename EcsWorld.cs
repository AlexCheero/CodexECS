using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.UInt32;

//TODO: cover with tests
namespace ECS
{
    static class EntityExtension
    {
        public const int BitSizeHalved = sizeof(EntityType) * 4;
        public const EntityType NullEntity = (1 << BitSizeHalved) - 1;
        
        static EntityExtension()
        {
            if (NullEntity.GetVersion() > 0)
                throw new EcsException("NullEntity should always have 0 version");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetId(this EntityType entity)
        {
            var id = entity << BitSizeHalved;
            id >>= BitSizeHalved;
            return id;
        }

        public static void SetId(this ref EntityType entity, EntityType id)
        {
            if (id.GetId() >= NullEntity)
                throw new EcsException("set overflow id");
            entity = id.GetId() | entity.GetVersion();
        }

        public static void SetNullId(this ref EntityType entity)
        {
            entity = NullEntity.GetId() | entity.GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this in EntityType entity)
        {
            return entity.GetId() == NullEntity.GetId();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetVersion(this EntityType entity)
        {
            return entity >> BitSizeHalved;
        }

        public static void IncrementVersion(this ref EntityType entity)
        {
            EntityType version = entity.GetVersion();
            version++;
            version <<= BitSizeHalved;
            entity = entity.GetId() | version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToIdx(this EntityType entity) => (int)entity.GetId();
    }

    class EcsException : Exception
    {
        public EcsException(string msg) : base(msg) { }
    }

    class EcsWorld
    {
        public EcsWorld(int entitiesReserved = 32)
        {
            _entites = new SimpleVector<EntityType>(entitiesReserved);
            _componentsPools = new Dictionary<Guid, IComponentsPool>();
        }

#region Entities methods
        private SimpleVector<EntityType> _entites;
        private EntityType _recycleListHead = EntityExtension.NullEntity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(EntityType entity)
        {
            return entity.GetId() < _entites.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref EntityType GetById(EntityType other)
        {
            if (other.IsNull() || !IsEnitityInRange(other))
                throw new EcsException("wrong entity");
            return ref _entites[other.ToIdx()];
        }

        public bool IsDead(EntityType entity)
        {
            return GetById(entity) != entity.GetId();
        }

        private ref EntityType GetRecycled()
        {
            ref var curr = ref _recycleListHead;
            ref var next = ref GetById(curr);
            while (!next.IsNull())
            {
                curr = ref next;
                next = ref GetById(next);
            }

            next.SetId(curr);
            next.IncrementVersion();
            curr.SetNullId();
            return ref next;
        }

        public void Delete(EntityType entity)
        {
            if (IsDead(entity))
                throw new EcsException("trying to delete already dead entity");
            if (!IsEnitityInRange(entity))
                throw new EcsException("trying to delete wrong entity");
            if (entity.IsNull())
                throw new EcsException("trying to delete null entity");
            ref var recycleListEnd = ref _recycleListHead;
            while (!recycleListEnd.IsNull())
                recycleListEnd = ref GetById(recycleListEnd);
            recycleListEnd.SetId(entity);
            GetById(entity).SetNullId();
        }

        public ref EntityType Create()
        {
            if (!_recycleListHead.IsNull())
                return ref GetRecycled();

            var lastEntity = (EntityType)_entites.Length;
            //TODO: wrap all throws in ifdef
            if (lastEntity == EntityExtension.NullEntity)
                throw new EcsException("entity limit reached");
            if (_entites.Length < 0)
                throw new EcsException("entities vector length overflow");
            if (lastEntity.GetVersion() > 0)
                throw new EcsException("lastEntity version should always be 0");
            
            _entites.Add(lastEntity);
            return ref _entites[_entites.Length - 1];
        }
        #endregion

#region Components methods
        interface IComponentsPool
        {
            public HashSet<int> EntitiesSet();
        }
        class ComponentsPool<T> : IComponentsPool
        {
            private SparseSet<T> _components;

            public ref T this[EntityType entity]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return ref _components[entity.ToIdx()]; } 
            }

            public ComponentsPool()
            {
                _components = new SparseSet<T>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Contains(EntityType entity) => _components.Contains(entity.ToIdx());

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref T Add(EntityType entity, T value) => ref _components.Add(entity.ToIdx(), value);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Remove(EntityType entity) => _components.Remove(entity.ToIdx());

            public HashSet<int> EntitiesSet() => _components._entitiesSet;
        }

        private Dictionary<Guid, IComponentsPool> _componentsPools;

        //TODO: not sure about the way to store pools and get keys for them
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Guid TypeKey<T>() { return default(T).GetType().GUID; }

        public bool HaveComponent<T>(EntityType entity)
        {
            var key = TypeKey<T>();
            if (!_componentsPools.ContainsKey(key))
                return false;
            var pool = _componentsPools[key] as ComponentsPool<T>;
            if (pool == null)
                throw new EcsException("invalid pool");
            return pool.Contains(entity);
        }

        public ref T AddComponent<T>(EntityType entity, T component = default)
        {
            //TODO: update filters on assign
            var key = TypeKey<T>();
            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new ComponentsPool<T>());
            var pool = _componentsPools[key] as ComponentsPool<T>;
            if (pool == null)
                throw new EcsException("invalid pool");
            return ref pool.Add(entity, component);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(EntityType entity)
        {
            var key = TypeKey<T>();
            var pool = _componentsPools[key] as ComponentsPool<T>;
            return ref pool[entity];
        }

        public void RemoveComponent<T>(EntityType entity)
        {
            var key = TypeKey<T>();
            var pool = _componentsPools[key] as ComponentsPool<T>;
            pool.Remove(entity);
        }
#endregion
#region Filters methods
        public void GetView(ref SimpleVector<int> filter, in Type[] types, in Type[] excludes)
        {
            filter.Clear();
            if (types.Length == 0)
                return;

            var firstSet = _componentsPools[types[0].GUID].EntitiesSet();
            foreach (var idx in firstSet)
            {
                bool belongs = true;
                for (int i = 1; i < types.Length; i++)
                {
                    var set = _componentsPools[types[i].GUID].EntitiesSet();
                    belongs &= set.Contains(idx);
                    if (!belongs)
                        goto LoopEnd;
                }
                for (int i = 0; i < excludes.Length; i++)
                {
                    var set = _componentsPools[excludes[i].GUID].EntitiesSet();
                    belongs &= !set.Contains(idx);
                    if (!belongs)
                        goto LoopEnd;
                }
            LoopEnd:
                if (belongs)
                    filter.Add(idx);
            }
        }
#endregion
    }
    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
