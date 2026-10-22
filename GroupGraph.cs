using System.Collections.Generic;

namespace CodexECS
{
    // Allocation groups keyed by an exact component mask, kept as a subset graph (root = empty mask).
    // A group decides only where new entities are placed: groups own whole pages, so entities created
    // with the same mask share pages. Groups never prune filters, and later component changes never
    // move an entity to another group.
    internal sealed class GroupGraph
    {
        internal sealed class Node
        {
            internal readonly BitMask Mask;
            internal readonly int Id;
            internal readonly List<Node> Children = new List<Node>();

            internal Node(in BitMask mask, int id)
            {
                // Owned copy: the caller's mask may change after this.
                Mask = mask.Duplicate();
                Id = id;
            }
        }

        // The empty mask: entities created without components.
        internal readonly Node Root;
        private readonly Dictionary<BitMask, Node> _exact = new Dictionary<BitMask, Node>(BitMask.MaskComparer);
        internal int Count { get; private set; }

        internal GroupGraph() => Root = new Node(new BitMask(), Count++);

        // Exact equality only: an existing subset or superset is never substituted.
        internal Node Get(in BitMask mask)
        {
            if (mask.SetBitsCount == 0)
                return Root;
            if (_exact.TryGetValue(mask, out var existing))
                return existing;

            Node parent = Root, exact = null;
            Find(Root, in mask, ref parent, ref exact);
            if (exact != null)
                return exact;
            var node = new Node(in mask, Count++);
            // Children of the new node's parent that are supersets of the new mask move under it.
            for (int i = parent.Children.Count - 1; i >= 0; i--)
            {
                var child = parent.Children[i];
                if (!child.Mask.InclusivePass(in mask))
                    continue;
                parent.Children.RemoveAt(i);
                node.Children.Add(child);
            }
            parent.Children.Add(node);
            _exact.Add(node.Mask, node);
            return node;
        }

        // Visits every branch whose mask is a subset of the request: overlapping branches are not greedy.
        private static void Find(Node node, in BitMask mask, ref Node parent, ref Node exact)
        {
            foreach (var child in node.Children)
            {
                if (!mask.InclusivePass(in child.Mask))
                    continue;
                if (mask.SetBitsCount == child.Mask.SetBitsCount)
                {
                    exact = child;
                    return;
                }
                if (child.Mask.SetBitsCount > parent.Mask.SetBitsCount)
                    parent = child;
                Find(child, in mask, ref parent, ref exact);
                if (exact != null)
                    return;
            }
        }
    }
}
