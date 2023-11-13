using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class SteeringPath
    {
        private List<WayPoint> nodes;

        int currentIndex;

        private float? totalLength;

        public bool Unreachable
        {
            get;
            set;
        }

        public float TotalLength
        {
            get
            {
                if (Unreachable) { return float.PositiveInfinity; }
                if (!totalLength.HasValue)
                {
                    CalculateTotalLength();
                }
                return totalLength.Value;
            }
        }

        public float GetLength(int? startIndex = null, int? endIndex = null)
        {
            if (Unreachable) { return float.PositiveInfinity; }
            startIndex ??= 0;
            endIndex ??= Nodes.Count - 1;
            if (startIndex == 0 && endIndex == Nodes.Count - 1)
            {
                return TotalLength;
            }
            if (!totalLength.HasValue)
            {
                CalculateTotalLength();
            }
            float length = 0.0f;
            for (int i = startIndex.Value; i < endIndex.Value; i++)
            {
                length += nodeDistances[i];
            }
            return length;
        }

        private void CalculateTotalLength()
        {
            totalLength = 0.0f;
            nodeDistances.Clear();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                float distance = Vector2.Distance(nodes[i].WorldPosition, nodes[i + 1].WorldPosition);
                totalLength += distance;
                nodeDistances.Add(distance);
            }
        }

        private readonly List<float> nodeDistances = new List<float>();

        public SteeringPath(bool unreachable = false)
        {
            nodes = new List<WayPoint>();
            Unreachable = unreachable;
        }

        public void AddNode(WayPoint node)
        {
            if (node == null) { return; }
            nodes.Add(node);

            if (node.CurrentHull == null) { HasOutdoorsNodes = true; }
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
                if (currentIndex - 1 < 0 || currentIndex - 1 > nodes.Count - 1) { return null; }
                return nodes[currentIndex - 1];
            }
        }

        public WayPoint CurrentNode
        {
            get 
            {
                if (currentIndex < 0 || currentIndex > nodes.Count - 1) { return null; }
                return nodes[currentIndex]; 
            }
        }

        public bool IsAtEndNode => currentIndex >= nodes.Count - 1;

        public List<WayPoint> Nodes
        {
            get { return nodes; }
        }

        public WayPoint NextNode
        {
            get
            {
                if (currentIndex + 1 < 0 || currentIndex + 1 > nodes.Count - 1) { return null; }
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

        public void SkipToNode(int nodeIndex)
        {
            currentIndex = nodeIndex;
        }

        public WayPoint CheckProgress(Vector2 simPosition, float minSimDistance = 0.1f)
        {
            if (nodes.Count == 0 || currentIndex > nodes.Count - 1) { return null; }
            if (Vector2.Distance(simPosition, nodes[currentIndex].SimPosition) < minSimDistance) { currentIndex++; }

            return CurrentNode;
        }

        public void ClearPath()
        {
            nodes.Clear();
        }
    }
}
