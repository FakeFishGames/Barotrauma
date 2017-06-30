using System.Collections.Generic;

namespace FarseerPhysics.Common.Decomposition.Seidel
{
    // Node for a Directed Acyclic graph (DAG)
    internal abstract class Node
    {
        protected Node LeftChild;
        public List<Node> ParentList;
        protected Node RightChild;

        protected Node(Node left, Node right)
        {
            ParentList = new List<Node>();
            LeftChild = left;
            RightChild = right;

            if (left != null)
                left.ParentList.Add(this);
            if (right != null)
                right.ParentList.Add(this);
        }

        public abstract Sink Locate(Edge s);

        // Replace a node in the graph with this node
        // Make sure parent pointers are updated
        public void Replace(Node node)
        {
            foreach (Node parent in node.ParentList)
            {
                // Select the correct node to replace (left or right child)
                if (parent.LeftChild == node)
                    parent.LeftChild = this;
                else
                    parent.RightChild = this;
            }
            ParentList.AddRange(node.ParentList);
        }
    }
}
