using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Subsurface
{
    class SteeringPath
    {
        private Queue<Vector2> nodes;

        const float MinDistance = 0.1f;

        Vector2 currentNode;

        public SteeringPath()
        {
            nodes = new Queue<Vector2>();
        }

        public void AddNode(Vector2 node)
        {
            if (node == Vector2.Zero) return;
            nodes.Enqueue(node);
        }

        public Vector2 CurrentNode
        {
            get { return currentNode; }
        }

        public Vector2 GetNode(Vector2 pos)
        {
            if (nodes.Count == 0) return Vector2.Zero;
            if (currentNode == Vector2.Zero || Vector2.Distance(pos, currentNode) < MinDistance) currentNode = nodes.Dequeue();

            return currentNode;
        }

        public void ClearPath()
        {
            nodes.Clear();
        }
    }
}
