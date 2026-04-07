using System.Runtime.CompilerServices;

namespace CodexECS
{
    public class GroupedComponents<T1>
    {
        private readonly ComponentsPool<T1> _pool1;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T1[] Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
            }

            return _pool1.Values;
        }
    }
    
    public class GroupedComponents<T1, T2>
    {
        private readonly ComponentsPool<T1> _pool1;
        private readonly ComponentsPool<T2> _pool2;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
            _pool2 = world.GetComponentsPool<T2>();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (T1[], T2[]) Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            var sparse2 = _pool2.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
                indices.i2 = sparse2[eid];
            }

            return (_pool1.Values, _pool2.Values);
        }
    }
    
    public class GroupedComponents<T1, T2, T3>
    {
        private readonly ComponentsPool<T1> _pool1;
        private readonly ComponentsPool<T2> _pool2;
        private readonly ComponentsPool<T3> _pool3;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
            _pool2 = world.GetComponentsPool<T2>();
            _pool3 = world.GetComponentsPool<T3>();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (T1[], T2[], T3[]) Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            var sparse2 = _pool2.Sparse;
            var sparse3 = _pool3.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
                indices.i2 = sparse2[eid];
                indices.i3 = sparse3[eid];
            }

            return (_pool1.Values, _pool2.Values, _pool3.Values);
        }
    }
    
    public class GroupedComponents<T1, T2, T3, T4>
    {
        private readonly ComponentsPool<T1> _pool1;
        private readonly ComponentsPool<T2> _pool2;
        private readonly ComponentsPool<T3> _pool3;
        private readonly ComponentsPool<T4> _pool4;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
            _pool2 = world.GetComponentsPool<T2>();
            _pool3 = world.GetComponentsPool<T3>();
            _pool4 = world.GetComponentsPool<T4>();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (T1[], T2[], T3[], T4[]) Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            var sparse2 = _pool2.Sparse;
            var sparse3 = _pool3.Sparse;
            var sparse4 = _pool4.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
                indices.i2 = sparse2[eid];
                indices.i3 = sparse3[eid];
                indices.i4 = sparse4[eid];
            }

            return (_pool1.Values, _pool2.Values, _pool3.Values, _pool4.Values);
        }
    }
    
    public class GroupedComponents<T1, T2, T3, T4, T5>
    {
        private readonly ComponentsPool<T1> _pool1;
        private readonly ComponentsPool<T2> _pool2;
        private readonly ComponentsPool<T3> _pool3;
        private readonly ComponentsPool<T4> _pool4;
        private readonly ComponentsPool<T5> _pool5;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
            _pool2 = world.GetComponentsPool<T2>();
            _pool3 = world.GetComponentsPool<T3>();
            _pool4 = world.GetComponentsPool<T4>();
            _pool5 = world.GetComponentsPool<T5>();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (T1[], T2[], T3[], T4[], T5[]) Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            var sparse2 = _pool2.Sparse;
            var sparse3 = _pool3.Sparse;
            var sparse4 = _pool4.Sparse;
            var sparse5 = _pool5.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
                indices.i2 = sparse2[eid];
                indices.i3 = sparse3[eid];
                indices.i4 = sparse4[eid];
                indices.i5 = sparse5[eid];
            }

            return (_pool1.Values, _pool2.Values, _pool3.Values, _pool4.Values, _pool5.Values);
        }
    }
    
    //T6, T7, T8
    
    public class GroupedComponents<T1, T2, T3, T4, T5, T6, T7, T8, T9>
    {
        private readonly ComponentsPool<T1> _pool1;
        private readonly ComponentsPool<T2> _pool2;
        private readonly ComponentsPool<T3> _pool3;
        private readonly ComponentsPool<T4> _pool4;
        private readonly ComponentsPool<T5> _pool5;
        private readonly ComponentsPool<T6> _pool6;
        private readonly ComponentsPool<T7> _pool7;
        private readonly ComponentsPool<T8> _pool8;
        private readonly ComponentsPool<T9> _pool9;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
            _pool2 = world.GetComponentsPool<T2>();
            _pool3 = world.GetComponentsPool<T3>();
            _pool4 = world.GetComponentsPool<T4>();
            _pool5 = world.GetComponentsPool<T5>();
            _pool6 = world.GetComponentsPool<T6>();
            _pool7 = world.GetComponentsPool<T7>();
            _pool8 = world.GetComponentsPool<T8>();
            _pool9 = world.GetComponentsPool<T9>();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (T1[], T2[], T3[], T4[], T5[], T6[], T7[], T8[], T9[]) Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            var sparse2 = _pool2.Sparse;
            var sparse3 = _pool3.Sparse;
            var sparse4 = _pool4.Sparse;
            var sparse5 = _pool5.Sparse;
            var sparse6 = _pool6.Sparse;
            var sparse7 = _pool7.Sparse;
            var sparse8 = _pool8.Sparse;
            var sparse9 = _pool9.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
                indices.i2 = sparse2[eid];
                indices.i3 = sparse3[eid];
                indices.i4 = sparse4[eid];
                indices.i5 = sparse5[eid];
                indices.i6 = sparse6[eid];
                indices.i7 = sparse7[eid];
                indices.i8 = sparse8[eid];
                indices.i9 = sparse9[eid];
            }

            return (_pool1.Values, _pool2.Values, _pool3.Values, _pool4.Values, _pool5.Values, _pool6.Values, _pool7.Values, _pool8.Values, _pool9.Values);
        }
    }
    
    public class GroupedComponents<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
    {
        private readonly ComponentsPool<T1> _pool1;
        private readonly ComponentsPool<T2> _pool2;
        private readonly ComponentsPool<T3> _pool3;
        private readonly ComponentsPool<T4> _pool4;
        private readonly ComponentsPool<T5> _pool5;
        private readonly ComponentsPool<T6> _pool6;
        private readonly ComponentsPool<T7> _pool7;
        private readonly ComponentsPool<T8> _pool8;
        private readonly ComponentsPool<T9> _pool9;
        private readonly ComponentsPool<T10> _pool10;

        public GroupedComponents(EcsWorld world)
        {
            _pool1 = world.GetComponentsPool<T1>();
            _pool2 = world.GetComponentsPool<T2>();
            _pool3 = world.GetComponentsPool<T3>();
            _pool4 = world.GetComponentsPool<T4>();
            _pool5 = world.GetComponentsPool<T5>();
            _pool6 = world.GetComponentsPool<T6>();
            _pool7 = world.GetComponentsPool<T7>();
            _pool8 = world.GetComponentsPool<T8>();
            _pool9 = world.GetComponentsPool<T9>();
            _pool10 = world.GetComponentsPool<T10>();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (T1[], T2[], T3[], T4[], T5[], T6[], T7[], T8[], T9[], T10[]) Group(EcsFilter filter)
        {
            var dense = filter.Dense;
            var count = filter.EntitiesCount;
            var packedIndices = PackedIndicesBuffer.GetBuffer(count);
            var sparse1 = _pool1.Sparse;
            var sparse2 = _pool2.Sparse;
            var sparse3 = _pool3.Sparse;
            var sparse4 = _pool4.Sparse;
            var sparse5 = _pool5.Sparse;
            var sparse6 = _pool6.Sparse;
            var sparse7 = _pool7.Sparse;
            var sparse8 = _pool8.Sparse;
            var sparse9 = _pool9.Sparse;
            var sparse10 = _pool10.Sparse;
            for (int i = 0; i < count; i++)
            {
                var eid = dense[i];
                ref var indices = ref packedIndices[i];
                indices.i1 = sparse1[eid];
                indices.i2 = sparse2[eid];
                indices.i3 = sparse3[eid];
                indices.i4 = sparse4[eid];
                indices.i5 = sparse5[eid];
                indices.i6 = sparse6[eid];
                indices.i7 = sparse7[eid];
                indices.i8 = sparse8[eid];
                indices.i9 = sparse9[eid];
                indices.i10 = sparse10[eid];
            }

            return (_pool1.Values, _pool2.Values, _pool3.Values, _pool4.Values, _pool5.Values, _pool6.Values, _pool7.Values, _pool8.Values, _pool9.Values, _pool10.Values);
        }
    }
}