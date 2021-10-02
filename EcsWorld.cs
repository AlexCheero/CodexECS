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
            _componentsPools = new Dictionary<Type, IComponentsPool>();
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
            return ref _entites[(int)other.GetId()];
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
        interface IComponentsPool { }
        class ComponentsPool<T> : IComponentsPool
        {
            private SparseSet<T> _components;

            public ref T this[EntityType entity]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return ref _components[(int)entity]; } 
            }

            public ComponentsPool()
            {
                _components = new SparseSet<T>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Contains(EntityType entity) => _components.Contains((int)entity);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref T Assign(EntityType entity, T value) => ref _components.Assign((int)entity, value);
        }

        private Dictionary<Type, IComponentsPool> _componentsPools;

        public bool HaveComponent<T>(EntityType entity)
        {
            var key = default(T).GetType();
            if (!_componentsPools.ContainsKey(key))
                return false;
            var pool = _componentsPools[key] as ComponentsPool<T>;
            if (pool == null)
                throw new EcsException("invalid pool");
            return pool.Contains(entity);
        }

        public ref T AssignComponent<T>(EntityType entity, T component = default)
        {
            //TODO: update filters on assign
            var key = component.GetType();
            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new ComponentsPool<T>());
            var pool = _componentsPools[key] as ComponentsPool<T>;
            if (pool == null)
                throw new EcsException("invalid pool");
            return ref pool.Assign(entity, component);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(EntityType entity)
        {
            //TODO: not sure about the way to store pools and get keys for them
            var key = default(T).GetType();
            var pool = _componentsPools[key] as ComponentsPool<T>;
            return ref pool[entity];
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
