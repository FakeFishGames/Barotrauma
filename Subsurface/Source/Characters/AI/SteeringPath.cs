using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Subsurface
{
    class SteeringPath
    {
        private Queue<WayPoint> nodes;
        
        WayPoint currentNode;

        public SteeringPath()
        {
            nodes = new Queue<WayPoint>();
        }

        public void AddNode(WayPoint node)
        {
            if (node == null) return;
            nodes.Enqueue(node);
        }

        public WayPoint CurrentNode
        {
            get { return currentNode; }
        }

        public WayPoint GetNode(Vector2 pos, float minDistance = 0.1f)
        {
            if (nodes.Count == 0) return null;
            if (currentNode == null || Vector2.Distance(pos, currentNode.SimPosition) < minDistance) currentNode = nodes.Dequeue();

            return currentNode;
        }

        public void ClearPath()
        {
            nodes.Clear();
        }
    }
}
