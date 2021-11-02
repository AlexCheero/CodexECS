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
                EcsExceptionThrower.ThrowException("NullEntity should always have 0 version");
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
                EcsExceptionThrower.ThrowException("set overflow id");
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
        public static int ToId(this EntityType entity) => (int)entity.GetId();

        //TODO: check versions of all entities that uses these methods
#region World methods forwarded
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AddComponent<T>(this EntityType entity, EcsWorld world, T component = default)
            => ref world.AddComponent<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddTag<T>(this EntityType entity, EcsWorld world) => world.AddTag<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDead(this EntityType entity, EcsWorld world) => world.IsDead(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Have<T>(this EntityType entity, EcsWorld world)
            => world.Have<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T GetComponent<T>(this EntityType entity, EcsWorld world)
            => ref world.GetComponent<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveComponent<T>(this EntityType entity, EcsWorld world)
            => world.RemoveComponent<T>(entity);
#endregion
    }

    class EcsException : Exception
    {
        public EcsException(string msg) : base(msg) { }
    }

    static class EcsExceptionThrower
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowException(string message)
        {
            throw new EcsException(message);
        }
    }

    class EcsWorld
    {
        public EcsWorld(int entitiesReserved = 32)
        {
            _entites = new SimpleVector<EntityType>(entitiesReserved);
            _componentsPools = new Dictionary<Type, IComponentsPool>();
            //TODO: don't forget to copy
            //_filtersGraph = new Node<Type, SimpleVector<int>>();
            _filters = new FiltersCollection();
        }

        public void Copy(in EcsWorld other)
        {
            _entites.Copy(other._entites);
            foreach (var key in _componentsPools.Keys)
            {
                if (!other._componentsPools.ContainsKey(key))
                {
                    _componentsPools[key].Clear();
                }
            }

            foreach (var key in other._componentsPools.Keys)
            {
                var otherPool = other._componentsPools[key];
                if (_componentsPools.ContainsKey(key))
                    _componentsPools[key].Copy(otherPool);
                else
                    _componentsPools.Add(key, otherPool.Dulicate());
            }
        }

#region Entities methods
        private SimpleVector<EntityType> _entites;
        private EntityType _recycleListHead = EntityExtension.NullEntity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(int id) => id < _entites.Length;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(EntityType entity) => IsEnitityInRange(entity.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref EntityType GetRefById(int id)
        {
            if (id == EntityExtension.NullEntity.GetId() || !IsEnitityInRange(id))
                EcsExceptionThrower.ThrowException("wrong entity id");
            return ref _entites[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref EntityType GetRefById(EntityType other) => ref GetRefById(other.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType GetById(int id) => GetRefById(id);

        public bool IsDead(int id) => GetRefById(id).GetId() != id;

        public bool IsDead(EntityType entity) => IsDead(entity.ToId());

        private ref EntityType GetRecycled()
        {
            ref var curr = ref _recycleListHead;
            ref var next = ref GetRefById(curr);
            while (!next.IsNull())
            {
                curr = ref next;
                next = ref GetRefById(next);
            }

            next.SetId(curr);
            next.IncrementVersion();
            curr.SetNullId();
            return ref next;
        }

        public void Delete(EntityType entity)
        {
            if (IsDead(entity))
                EcsExceptionThrower.ThrowException("trying to delete already dead entity");
            if (!IsEnitityInRange(entity))
                EcsExceptionThrower.ThrowException("trying to delete wrong entity");
            if (entity.IsNull())
                EcsExceptionThrower.ThrowException("trying to delete null entity");
            ref var recycleListEnd = ref _recycleListHead;
            while (!recycleListEnd.IsNull())
                recycleListEnd = ref GetRefById(recycleListEnd);
            recycleListEnd.SetId(entity);
            GetRefById(entity).SetNullId();
        }

        public EntityType Create()
        {
            if (!_recycleListHead.IsNull())
                return GetRecycled();

            var lastEntity = (EntityType)_entites.Length;
            if (lastEntity == EntityExtension.NullEntity)
                EcsExceptionThrower.ThrowException("entity limit reached");
            if (_entites.Length < 0)
                EcsExceptionThrower.ThrowException("entities vector length overflow");
            if (lastEntity.GetVersion() > 0)
                EcsExceptionThrower.ThrowException("lastEntity version should always be 0");
            
            _entites.Add(lastEntity);
            return _entites[_entites.Length - 1];
        }
#endregion

#region Components methods
        private Dictionary<Type, IComponentsPool> _componentsPools;

        //TODO: not sure about the way to store pools and get keys for them
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Type TypeKey<T>() => default(T).GetType();

        public bool Have<T>(EntityType entity)
        {
            var key = TypeKey<T>();
            if (!_componentsPools.ContainsKey(key))
                return false;
            if (_componentsPools[key] as ComponentsPool<T> == null
                && _componentsPools[key] as TagsPool<T> == null)
            {
                EcsExceptionThrower.ThrowException("invalid pool");
            }
            return _componentsPools[key].Contains(entity);
        }

        public ref T AddComponent<T>(EntityType entity, T component = default)
        {
            /*
             * get all FiltersTrieLeaf this entity belongs to
             * check if any of their excludes contains components type
             * update exclude filters
             * add entity to filter with new type of component
             */
            var key = TypeKey<T>();
            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new ComponentsPool<T>());
            var pool = _componentsPools[key] as ComponentsPool<T>;
            if (pool == null)
                EcsExceptionThrower.ThrowException("invalid pool");
            return ref pool.Add(entity, component);
        }

        public void AddTag<T>(EntityType entity)
        {
            //same as for AddComponent

            var key = TypeKey<T>();
            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new TagsPool<T>());
            var pool = _componentsPools[key] as TagsPool<T>;
            if (pool == null)
                EcsExceptionThrower.ThrowException("invalid pool");
            pool.Add(entity);
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
            /*
             * get from FitersTrie all filters this entity belongs to
             * remove entity from this filters
             * get all FiltersTrieLeaf which still contains entity
             * update exclude filters
             */
            _componentsPools[TypeKey<T>()].Remove(entity);
        }
        #endregion
        #region Filters methods
        //private GraphNode<Type, SimpleVector<int>> _filtersGraph;

        private FiltersCollection _filters;

        public void RegisterFilter(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            _filters.TryAdd(comps, excludes);
        }

        //TODO: maybe shouldn't use HashSet and use sorted array with uniqueness check
        public HashSet<int> GetView(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            return _filters.Get(comps, excludes);
        }
        //TODO: implement sortable groups
        //TODO: probably it is better to register all needed views at start, and update them on adding/removing
        //      components, using some kind of filter graph (somewhat similar to flecs)
        public void GetView(ref SimpleVector<int> filter, in Type[] types, in Type[] excludes = null)
        {
            filter.Clear();
            if (types.Length == 0)
                return;

            var firstPool = _componentsPools[types[0]];
            for (int i = 0; i < firstPool.Length; i++)
            {
                bool belongs = true;
                var id = firstPool.IthEntityId(i);
                if (IsDead(id))
                    continue;

                for (int j = 1; j < types.Length && belongs; j++)
                {
                    var pool = _componentsPools[types[j]];
                    belongs &= pool.Contains(id);
                }
                for (int j = 0; excludes != null && j < excludes.Length && belongs; j++)
                {
                    var pool = _componentsPools[excludes[j]];
                    belongs &= !pool.Contains(id);
                }
                if (belongs)
                    filter.Add(id);
            }
        }
#endregion
    }

    //usage example:
    class Program
    {
        struct Comp1 { public int i; }
        struct Comp2 { public float f; }
        struct Comp3 { public uint ui; }
        struct Tag1 { }
        struct Tag2 { }
        struct Tag3 { }

        static void Main(string[] args)
        {
            var world = new EcsWorld();

            var entity1 = world.Create();
            entity1.AddComponent<Comp1>(world).i = 10;
            entity1.AddComponent<Comp2>(world).f = 0.5f;
            entity1.AddTag<Tag1>(world);
            entity1.AddTag<Tag2>(world);

            var entity2 = world.Create();
            entity2.AddComponent<Comp1>(world).i = 10;
            entity2.AddComponent<Comp2>(world).f = 0.5f;
            entity2.AddTag<Tag2>(world);
            entity2.AddTag<Tag3>(world);

            var entity3 = world.Create();
            entity3.AddComponent<Comp1>(world).i = 10;
            entity3.AddComponent<Comp2>(world).f = 0.5f;
            entity3.AddTag<Tag1>(world);

            var filter = new SimpleVector<int>();

            Type GetType<T>() => default(T).GetType();

            var comps = new Type[] { GetType<Comp1>(), GetType<Comp2>(), GetType<Tag1>(), GetType<Tag2>() };
            var excludes = new Type[] { };
            world.GetView(ref filter, in comps, in excludes);//should be only 0

            comps = new Type[] { GetType<Comp2>() };
            excludes = new Type[] { };
            world.GetView(ref filter, in comps, in excludes);//should be all

            comps = new Type[] { GetType<Comp1>(), GetType<Comp2>(), GetType<Tag2>() };
            excludes = new Type[] { GetType<Tag1>() };
            world.GetView(ref filter, in comps, in excludes);//should be only 1

            for(int i = 0; i < filter.Length; i++)
            {
                var id = filter[i];
                var entity = world.GetById(id);
                var comp1 = entity.GetComponent<Comp1>(world);
                ref var comp2 = ref entity.GetComponent<Comp2>(world);
                if (!entity.Have<Comp3>(world))
                {
                    //do smth
                    int a = 0;
                }
                if (entity.Have<Tag3>(world))
                {
                    //do smth
                    int a = 0;
                }
            }

            var world2 = new EcsWorld();
            world2.Copy(world);
        }
    }
}
