namespace FarseerPhysics.Common.Decomposition.Seidel
{
    internal class XNode : Node
    {
        private Point _point;

        public XNode(Point point, Node lChild, Node rChild)
            : base(lChild, rChild)
        {
            _point = point;
        }

        public override Sink Locate(Edge edge)
        {
            if (edge.P.X >= _point.X)
                return RightChild.Locate(edge); // Move to the right in the graph

            return LeftChild.Locate(edge); // Move to the left in the graph
        }
    }
}