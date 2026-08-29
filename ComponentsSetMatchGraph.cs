using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

namespace CodexECS
{
    /// <summary>
    /// Stores component-set subscriptions as a subset tree. Every edge points from a
    /// less-specific mask to a strict superset, so an unsatisfied node also prunes its
    /// complete subtree during matching.
    /// </summary>
    internal sealed class ComponentsSetMatchGraph
    {
        internal sealed class Node
        {
            internal readonly List<Node> ChildrenList;

            internal readonly BitMask RequiredMask;
            internal Node Parent { get; private set; }
            internal IReadOnlyList<Node> Children => ChildrenList;
            internal Action<EcsWorld> Callback { get; private set; }
            internal BitMask PendingEntities;
            internal bool IsQueued;

            internal Node(in BitMask requiredMask, Node parent, Action<EcsWorld> callback)
            {
                RequiredMask = requiredMask.Duplicate();
                Parent = parent;
                Callback = callback;
                ChildrenList = new List<Node>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void AppendCallback(Action<EcsWorld> callback) => Callback += callback;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Invoke(EcsWorld world) => Callback?.Invoke(world);

            internal void AddChild(Node child)
            {
                child.Parent = this;
                ChildrenList.Add(child);
            }

            internal void ReparentDirectSupersetsTo(Node newParent)
            {
                // Walk backwards and insert moved nodes at the front. This preserves their
                // previous sibling order while avoiding a temporary collection.
                for (var i = ChildrenList.Count - 1; i >= 0; i--)
                {
                    var child = ChildrenList[i];
                    if (!child.RequiredMask.InclusivePass(newParent.RequiredMask))
                        continue;

                    ChildrenList.RemoveAt(i);
                    child.Parent = newParent;
                    newParent.ChildrenList.Insert(0, child);
                }
            }
        }

        private readonly Node _root;

        internal ComponentsSetMatchGraph()
        {
            var emptyMask = new BitMask();
            _root = new Node(emptyMask, null, null);
        }

        /// <summary>
        /// Adds a normalized required-components mask. Mask normalization (for example,
        /// removing MatchReact) belongs to the caller.
        /// </summary>
        internal Node Subscribe(in BitMask requiredMask, Action<EcsWorld> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            if (requiredMask.SetBitsCount == 0)
            {
                _root.AppendCallback(callback);
                return _root;
            }

            var closestParent = _root;
            Node exactNode = null;
            var closestDistance = requiredMask.SetBitsCount;
            FindClosestSubset(_root, requiredMask, ref closestParent, ref closestDistance, ref exactNode);

            if (exactNode != null)
            {
                exactNode.AppendCallback(callback);
                return exactNode;
            }

            var node = new Node(requiredMask, closestParent, callback);
            closestParent.ReparentDirectSupersetsTo(node);
            closestParent.AddChild(node);
            return node;
        }

        /// <summary>
        /// Reports every subscription satisfied by <paramref name="componentsMask"/>.
        /// Results are parent-before-child and the supplied buffer is reused.
        /// </summary>
        internal void CollectMatches(in BitMask componentsMask, List<Node> matches)
        {
            if (matches == null)
                throw new ArgumentNullException(nameof(matches));

            matches.Clear();
            if (_root.Callback != null)
                matches.Add(_root);
            CollectSatisfiedChildren(_root, componentsMask, matches);
        }

        /// <summary>
        /// Reports subscriptions that are satisfied by the current mask but were not
        /// satisfied by the previous mask. This is the transition-aware form used by
        /// component-add and bulk-create paths.
        /// </summary>
        internal void CollectNewMatches(
            in BitMask previousComponentsMask,
            in BitMask componentsMask,
            List<Node> matches)
        {
            if (matches == null)
                throw new ArgumentNullException(nameof(matches));

            matches.Clear();
            CollectNewlySatisfiedChildren(
                _root,
                previousComponentsMask,
                componentsMask,
                matches);
        }

        private static void FindClosestSubset(
            Node node,
            in BitMask requiredMask,
            ref Node closestParent,
            ref int closestDistance,
            ref Node exactNode)
        {
            var children = node.ChildrenList;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!requiredMask.InclusivePass(child.RequiredMask))
                    continue;

                var distance = requiredMask.SetBitsCount - child.RequiredMask.SetBitsCount;
                if (distance == 0)
                {
                    exactNode = child;
                    return;
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestParent = child;
                }

                FindClosestSubset(
                    child,
                    requiredMask,
                    ref closestParent,
                    ref closestDistance,
                    ref exactNode);
                if (exactNode != null)
                    return;
            }
        }

        private static void CollectSatisfiedChildren(
            Node node,
            in BitMask componentsMask,
            List<Node> matches)
        {
            var children = node.ChildrenList;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!componentsMask.InclusivePass(child.RequiredMask))
                    continue;

                matches.Add(child);
                CollectSatisfiedChildren(child, componentsMask, matches);
            }
        }

        private static void CollectNewlySatisfiedChildren(
            Node node,
            in BitMask previousComponentsMask,
            in BitMask componentsMask,
            List<Node> matches)
        {
            var children = node.ChildrenList;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!componentsMask.InclusivePass(child.RequiredMask))
                    continue;

                if (!previousComponentsMask.InclusivePass(child.RequiredMask))
                    matches.Add(child);

                // A parent can have matched before this transition while a more-specific
                // descendant becomes satisfied now, so always descend through a satisfied
                // branch rather than only through newly matched nodes.
                CollectNewlySatisfiedChildren(
                    child,
                    previousComponentsMask,
                    componentsMask,
                    matches);
            }
        }
    }
}
