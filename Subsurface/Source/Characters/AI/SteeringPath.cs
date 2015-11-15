using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class SteeringPath
    {
        private List<WayPoint> nodes;

        int currentIndex;

        public SteeringPath()
        {
            nodes = new List<WayPoint>();
        }

        public void AddNode(WayPoint node)
        {
            if (node == null) return;
            nodes.Add(node);
        }

        public WayPoint CurrentNode
        {
            get 
            {
                if (currentIndex < 0 || currentIndex > nodes.Count - 1) return null;
                return nodes[currentIndex]; 
            }
        }

        public List<WayPoint> Nodes
        {
            get { return nodes; }
        }

        public WayPoint NextNode
        {
            get
            {
                if (currentIndex+1 < 0 || currentIndex+1 > nodes.Count - 1) return null;
                return nodes[currentIndex+1];
            }
        }

        public void SkipToNextNode()
        {
            currentIndex++;
        }

        public WayPoint CheckProgress(Vector2 simPosition, float minSimDistance = 0.1f)
        {
            if (nodes.Count == 0 || currentIndex>nodes.Count-1) return null;
            if (Vector2.Distance(simPosition, nodes[currentIndex].SimPosition) < minSimDistance) currentIndex++;

            return CurrentNode;
        }

        public void ClearPath()
        {
            nodes.Clear();
        }
    }
}
