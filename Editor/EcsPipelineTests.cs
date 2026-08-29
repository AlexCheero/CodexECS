#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class EcsPipelineTests
    {
        private enum Group
        {
            Init = 3,
            Update = 27
        }

        private struct A : IComponent { }
        private struct B : IComponent { }

        private struct AlwaysMarker { }
        private struct EmptyMarker { }
        private struct AnyMarker { }
        private struct ProducerMarker { }
        private struct RemoverMarker { }
        private struct ConsumerMarker { }
        private struct ExcludedMarker { }
        private struct IncludedMarker { }
        private struct ParentMarker { }
        private struct DerivativeMarker { }
        private struct InitMarker { }
        private struct NormalMarker { }
        private struct NonPausableMarker { }
        private struct ThrowingMarker { }

        private sealed class ActionSystem<TMarker> : EcsSystem
        {
            private readonly Action<EcsWorld> _init;
            private readonly Action<EcsWorld> _tick;

            public int InitCalls { get; private set; }
            public int TickCalls { get; private set; }

            public ActionSystem(Action<EcsWorld> init = null, Action<EcsWorld> tick = null)
            {
                _init = init;
                _tick = tick;
            }

            public override void Init(EcsWorld world)
            {
                InitCalls++;
                _init?.Invoke(world);
            }

            public override void Tick(EcsWorld world)
            {
                TickCalls++;
                _tick?.Invoke(world);
            }
        }

        private sealed class DisposableSystem : EcsSystem, IDisposable
        {
            public int DisposeCalls { get; private set; }
            public void Dispose() => DisposeCalls++;
        }

        [Test]
        public void Tick_SkipsOnlySystemsWhoseEveryCapturedFilterIsEmpty()
        {
            var world = new EcsWorld();
            using var pipeline = new EcsPipeline<Group>(world, Group.Update);

            var empty = (ActionSystem<EmptyMarker>)pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().Build();
                return new ActionSystem<EmptyMarker>();
            }).System;
            var always = (ActionSystem<AlwaysMarker>)pipeline.Register(
                Group.Update,
                _ => new ActionSystem<AlwaysMarker>()).System;
            var anyRegistration = pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().Build();
                ecs.Filter().With<A>().Build();
                ecs.Filter().With<B>().Build();
                return new ActionSystem<AnyMarker>();
            });
            var any = (ActionSystem<AnyMarker>)anyRegistration.System;

            Assert.AreEqual(2, anyRegistration.Filters.Count,
                "Resolving a shared filter twice in one factory must only capture it once.");

            pipeline.Tick(Group.Update);
            Assert.AreEqual(0, empty.TickCalls);
            Assert.AreEqual(1, always.TickCalls);
            Assert.AreEqual(0, any.TickCalls);

            var entity = world.Create();
            world.Add<B>(entity);
            pipeline.Tick(Group.Update);

            Assert.AreEqual(0, empty.TickCalls);
            Assert.AreEqual(2, always.TickCalls);
            Assert.AreEqual(1, any.TickCalls,
                "A system using several filters runs when any one of them has entities.");
        }

        [Test]
        public void Tick_ObservesFilterBecomingNonemptyEarlierInTheSameGroup()
        {
            var world = new EcsWorld();
            using var pipeline = new EcsPipeline<Group>(world, Group.Update);
            var produced = false;

            pipeline.Register(Group.Update, _ => new ActionSystem<ProducerMarker>(tick: ecs =>
            {
                if (produced)
                    return;

                produced = true;
                var entity = ecs.Create();
                ecs.Add<A>(entity);
            }));
            var consumer = (ActionSystem<ConsumerMarker>)pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().Build();
                return new ActionSystem<ConsumerMarker>();
            }).System;

            pipeline.Tick(Group.Update);

            Assert.AreEqual(1, consumer.TickCalls);
        }

        [Test]
        public void Tick_ObservesFilterBecomingEmptyEarlierInTheSameGroup()
        {
            var world = new EcsWorld();
            var entity = world.Create();
            world.Add<A>(entity);
            using var pipeline = new EcsPipeline<Group>(world, Group.Update);
            EcsFilter filter = null;

            var remover = (ActionSystem<RemoverMarker>)pipeline.Register(Group.Update, ecs =>
            {
                filter = ecs.Filter().With<A>().Build();
                return new ActionSystem<RemoverMarker>(tick: tickWorld =>
                    tickWorld.Remove<A>(filter[0]));
            }).System;
            var consumerRegistration = pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().Build();
                return new ActionSystem<ConsumerMarker>();
            });
            var consumer = (ActionSystem<ConsumerMarker>)consumerRegistration.System;

            Assert.AreSame(filter, consumerRegistration.Filters[0],
                "Every factory must capture an already registered shared filter.");

            pipeline.Tick(Group.Update);

            Assert.AreEqual(1, remover.TickCalls);
            Assert.AreEqual(0, filter.EntitiesCount);
            Assert.AreEqual(0, consumer.TickCalls);
        }

        [Test]
        public void Graph_DoesNotPruneFiltersWithIncompatibleExcludeMasks()
        {
            var world = new EcsWorld();
            var entity = world.Create();
            world.Add<A>(entity);
            world.Add<B>(entity);
            using var pipeline = new EcsPipeline<Group>(world, Group.Update);

            var excludesB = (ActionSystem<ExcludedMarker>)pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().Without<B>().Build();
                return new ActionSystem<ExcludedMarker>();
            }).System;
            var includesB = (ActionSystem<IncludedMarker>)pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().With<B>().Build();
                return new ActionSystem<IncludedMarker>();
            }).System;

            pipeline.Tick(Group.Update);

            Assert.AreEqual(0, excludesB.TickCalls);
            Assert.AreEqual(1, includesB.TickCalls,
                "An empty Without<B> filter cannot be an ancestor of a With<B> filter.");
        }

        [Test]
        public void Graph_ParentAndDerivativeAreBothSkippedWhenParentIsEmpty()
        {
            var world = new EcsWorld();
            using var pipeline = new EcsPipeline<Group>(world, Group.Update);

            var parent = (ActionSystem<ParentMarker>)pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().Build();
                return new ActionSystem<ParentMarker>();
            }).System;
            var derivative = (ActionSystem<DerivativeMarker>)pipeline.Register(Group.Update, ecs =>
            {
                ecs.Filter().With<A>().With<B>().Build();
                return new ActionSystem<DerivativeMarker>();
            }).System;

            pipeline.Tick(Group.Update);

            Assert.AreEqual(0, parent.TickCalls);
            Assert.AreEqual(0, derivative.TickCalls);
        }

        [Test]
        public void InitializeAndPause_RespectMutableFlagsWithoutFilterPruningInit()
        {
            var world = new EcsWorld();
            using var pipeline = new EcsPipeline<Group>(world, Group.Init, Group.Update);

            var initRegistration = pipeline.Register(Group.Init, ecs =>
            {
                ecs.Filter().With<A>().Build();
                return new ActionSystem<InitMarker>();
            });
            var init = (ActionSystem<InitMarker>)initRegistration.System;
            pipeline.Initialize(Group.Init);
            Assert.AreEqual(1, init.InitCalls,
                "Initialization must run even when all of the system's filters are empty.");

            initRegistration.Active = false;
            pipeline.Initialize(Group.Init);
            Assert.AreEqual(1, init.InitCalls);

            var normal = (ActionSystem<NormalMarker>)pipeline.Register(
                Group.Update,
                _ => new ActionSystem<NormalMarker>()).System;
            var nonPausableRegistration = pipeline.Register(
                Group.Update,
                _ => new ActionSystem<NonPausableMarker>(),
                nonPausable: true);
            var nonPausable = (ActionSystem<NonPausableMarker>)nonPausableRegistration.System;

            pipeline.Pause();
            pipeline.Tick(Group.Update);
            Assert.AreEqual(0, normal.TickCalls);
            Assert.AreEqual(1, nonPausable.TickCalls);

            nonPausableRegistration.Active = false;
            pipeline.Tick(Group.Update, force: true);
            Assert.AreEqual(1, normal.TickCalls);
            Assert.AreEqual(1, nonPausable.TickCalls);

            pipeline.Unpause();
            Assert.IsFalse(pipeline.IsPaused);
        }

        [Test]
        public void Tick_UnlocksWorldInFinallyAndDisposeOwnsSharedSystemsOnce()
        {
            var world = new EcsWorld();
            var reactions = 0;
            world.SubscribeOnAdd<A>(_ => reactions++);
            var pipeline = new EcsPipeline<Group>(world, Group.Init, Group.Update);

            pipeline.Register(Group.Update, _ => new ActionSystem<ThrowingMarker>(tick: ecs =>
            {
                var entity = ecs.Create();
                ecs.Add<A>(entity);
                throw new InvalidOperationException("Expected test failure.");
            }));

            Assert.Throws<InvalidOperationException>(() => pipeline.Tick(Group.Update));
            Assert.AreEqual(1, reactions,
                "The per-system finally block must flush reactive work even when Tick throws.");

            var disposable = new DisposableSystem();
            pipeline.Register(Group.Init, _ => disposable);
            pipeline.Register(Group.Update, _ => disposable);
            pipeline.Dispose();
            pipeline.Dispose();

            Assert.AreEqual(1, disposable.DisposeCalls);
        }

        [Test]
        public void Groups_AreExplicitAndPreserveRegistrationOrder()
        {
            var world = new EcsWorld();
            using var pipeline = new EcsPipeline<Group>(world, Group.Update);
            var order = new List<int>();

            pipeline.Register(Group.Update, _ =>
                new ActionSystem<AlwaysMarker>(tick: __ => order.Add(1)));
            pipeline.Register(Group.Update, _ =>
                new ActionSystem<NormalMarker>(tick: __ => order.Add(2)));

            pipeline.Tick(Group.Update);

            CollectionAssert.AreEqual(new[] { 1, 2 }, order);
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Tick(Group.Init));
        }
    }
}
#endif
