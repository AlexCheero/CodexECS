namespace ECS
{
    class SubsetsGraph<K, V>
    {
        private class Node
        {
            private K[] _indetifiers;
            private V _value;

            private SimpleVector<Node> _next;
            private SimpleVector<Node> _prev;

            public Node Get(K[] indetifiers)
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

            private Node Move(K[] indetifiersDiff, bool forward)
            {
                Node Step(Node from, SimpleVector<Node> nodes, K step)
                {
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var diff = from._indetifiers/*.Diff(nodes[i]._indetifiers)*/;
                        //check that diff.Length is always 1
                        if (diff[0].Equals(step))
                            return nodes[i];
                    }

                    return null;
                }

                //forward = !_indetifiers.Contains(indetifiersDiff[0]);???
                Node destination = this;
                for (int i = 0; i < indetifiersDiff.Length; i++)
                    //check destination for null
                    destination = Step(destination, forward ? _next : _prev, indetifiersDiff[i]);

                return destination;
            }
        }

        private SimpleVector<SimpleVector<Node>> _nodes;
    }

    
}
