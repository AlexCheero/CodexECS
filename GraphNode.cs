namespace ECS
{
    class GraphNode<T>
    {
        private T[] _indetifiers;

        private GraphNode<T>[] _next;
        private GraphNode<T>[] _prev;

        public GraphNode<T> Get(T[] indetifiers)
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

        private GraphNode<T> Move(T[] indetifiersDiff, bool forward)
        {
            GraphNode<T> Step(GraphNode<T> from, GraphNode<T>[] nodes, T step)
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
            GraphNode<T> destination = this;
            for (int i = 0; i < indetifiersDiff.Length; i++)
                //check destination for null
                destination = Step(destination, forward ? _next : _prev, indetifiersDiff[i]);

            return destination;
        }
    }
}
