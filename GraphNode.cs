namespace ECS
{
    class GraphNode<K, V>
    {
        private K[] _indetifiers;
        private V _value;

        private GraphNode<K, V>[] _next;
        private GraphNode<K, V>[] _prev;

        public GraphNode<K, V> Get(K[] indetifiers)
        {
            //check real equality
            if (_indetifiers == indetifiers)
                return this;//same node

            var intersect = _indetifiers/*.Intersect(indetifiers)*/;
            if (intersect == _indetifiers)
                return Move(_indetifiers/*.Diff(indetifiers)*/, true);//forward node

            //backward and then forward node
            return Move(_indetifiers/*.Diff(intersect)*/, false).Move(intersect/*.Diff(indetifiers)*/, true);
        }

        private GraphNode<K, V> Move(K[] indetifiersDiff, bool forward)
        {
            GraphNode<K, V> Step(GraphNode<K, V> from, GraphNode<K, V>[] nodes, K step)
            {
                foreach (var node in nodes)
                {
                    var diff = from._indetifiers/*.Diff(node._indetifiers)*/;
                    //check that diff.Length is always 1
                    if (diff[0].Equals(step))
                        return node;
                }

                return null;
            }

            //forward = !_indetifiers.Contains(indetifiersDiff[0]);???
            GraphNode<K, V> destination = this;
            for (int i = 0; i < indetifiersDiff.Length; i++)
                //check destination for null
                destination = Step(destination, forward ? _next : _prev, indetifiersDiff[i]);

            return destination;
        }
    }
}
