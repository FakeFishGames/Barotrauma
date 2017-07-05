using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    class SteeringPath
    {
        private List<WayPoint> nodes;

        int currentIndex;

        public bool Unreachable
        {
            get;
            set;
        }

        public SteeringPath(bool unreachable = false)
        {
            nodes = new List<WayPoint>();
            Unreachable = unreachable;
        }

        public void AddNode(WayPoint node)
        {
            if (node == null) return;
            nodes.Add(node);

            if (node.CurrentHull == null) HasOutdoorsNodes = true;
        }
        
        public bool HasOutdoorsNodes
        {
            get;
            private set;
        }

        public int CurrentIndex
        {
            get { return currentIndex; }
        }

        public float Cost
        {
            get;
            set;
        }

        public WayPoint PrevNode
        {
            get 
            {
                if (currentIndex-1 < 0 || currentIndex-1 > nodes.Count - 1) return null;
                return nodes[currentIndex-1]; 
            }
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

        public bool Finished
        {
            get { return currentIndex >= nodes.Count; }
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
