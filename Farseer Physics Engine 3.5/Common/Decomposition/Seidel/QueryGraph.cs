using System.Collections.Generic;

namespace FarseerPhysics.Common.Decomposition.Seidel
{
    // Directed Acyclic graph (DAG)
    // See "Computational Geometry", 3rd edition, by Mark de Berg et al, Chapter 6.2
    internal class QueryGraph
    {
        private Node _head;

        public QueryGraph(Node head)
        {
            _head = head;
        }

        private Trapezoid Locate(Edge edge)
        {
            return _head.Locate(edge).Trapezoid;
        }

        public List<Trapezoid> FollowEdge(Edge edge)
        {
            List<Trapezoid> trapezoids = new List<Trapezoid>();
            trapezoids.Add(Locate(edge));
            int j = 0;

            while (edge.Q.X > trapezoids[j].RightPoint.X)
            {
                if (edge.IsAbove(trapezoids[j].RightPoint))
                {
                    trapezoids.Add(trapezoids[j].UpperRight);
                }
                else
                {
                    trapezoids.Add(trapezoids[j].LowerRight);
                }
                j += 1;
            }
            return trapezoids;
        }

        private void Replace(Sink sink, Node node)
        {
            if (sink.ParentList.Count == 0)
                _head = node;
            else
                node.Replace(sink);
        }

        public void Case1(Sink sink, Edge edge, Trapezoid[] tList)
        {
            YNode yNode = new YNode(edge, Sink.Isink(tList[1]), Sink.Isink(tList[2]));
            XNode qNode = new XNode(edge.Q, yNode, Sink.Isink(tList[3]));
            XNode pNode = new XNode(edge.P, Sink.Isink(tList[0]), qNode);
            Replace(sink, pNode);
        }

        public void Case2(Sink sink, Edge edge, Trapezoid[] tList)
        {
            YNode yNode = new YNode(edge, Sink.Isink(tList[1]), Sink.Isink(tList[2]));
            XNode pNode = new XNode(edge.P, Sink.Isink(tList[0]), yNode);
            Replace(sink, pNode);
        }

        public void Case3(Sink sink, Edge edge, Trapezoid[] tList)
        {
            YNode yNode = new YNode(edge, Sink.Isink(tList[0]), Sink.Isink(tList[1]));
            Replace(sink, yNode);
        }

        public void Case4(Sink sink, Edge edge, Trapezoid[] tList)
        {
            YNode yNode = new YNode(edge, Sink.Isink(tList[0]), Sink.Isink(tList[1]));
            XNode qNode = new XNode(edge.Q, yNode, Sink.Isink(tList[2]));
            Replace(sink, qNode);
        }
    }
}
