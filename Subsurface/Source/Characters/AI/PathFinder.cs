using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class PathNode
    {
        private WayPoint wayPoint;

        private int wayPointID;

        public int state;

        public PathNode Parent;
        
        private Vector2 position;

        public float F,G,H;

        public List<PathNode> connections;
        public float[] distances;
        
        public WayPoint Waypoint
        {
            get { return wayPoint; }
        }

        public Vector2 Position
        {
            get {return position;}
        }

        public PathNode(WayPoint wayPoint)
        {
            this.wayPoint = wayPoint;
            this.position = wayPoint.SimPosition;
            wayPointID = wayPoint.ID;

            connections = new List<PathNode>();
        }

        public static List<PathNode> GenerateNodes(List<WayPoint> wayPoints)
        {
            var nodes = new Dictionary<int, PathNode>();
            foreach (WayPoint wayPoint in wayPoints)
            {
                nodes.Add(wayPoint.ID, new PathNode(wayPoint));
            }

            foreach (KeyValuePair<int,PathNode> node in nodes)
            {
                foreach (MapEntity linked in node.Value.wayPoint.linkedTo)
                {
                    PathNode connectedNode = null;
                    nodes.TryGetValue(linked.ID, out connectedNode);
                    if (connectedNode == null) continue;

                    node.Value.connections.Add(connectedNode);                    
                }
            }

            var nodeList = nodes.Values.ToList();
            foreach (PathNode node in nodeList)
            {
                node.distances = new float[node.connections.Count];
                for (int i = 0; i< node.distances.Length; i++)
                {
                    node.distances[i] = Vector2.Distance(node.position, node.connections[i].position);
                }                
            }
            return nodeList;            
        }

    }

    class PathFinder
    {
        public delegate float? GetNodePenaltyHandler(PathNode node, PathNode prevNode);
        public GetNodePenaltyHandler GetNodePenalty;

        List<PathNode> nodes;

        private bool insideSubmarine;

        public PathFinder(List<WayPoint> wayPoints, bool insideSubmarine = false)
        {
            nodes = PathNode.GenerateNodes(wayPoints.FindAll(w => w.MoveWithLevel != insideSubmarine));

            this.insideSubmarine = insideSubmarine;
        }

        public SteeringPath FindPath(Vector2 start, Vector2 end)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            
            float closestDist = 0.0f;
            PathNode startNode = null;
            foreach (PathNode node in nodes)
            {
                float dist = System.Math.Abs(start.X-node.Position.X)+
                    System.Math.Abs(start.Y - node.Position.Y)*10.0f +
                    Vector2.Distance(end,node.Position)/2.0f;
                if (dist<closestDist || startNode==null)
                {
                    if (insideSubmarine && Submarine.CheckVisibility(start, node.Position) != null) continue;

                    closestDist = dist;
                    startNode = node;
                }
            }

            if (startNode == null)
            {
                DebugConsole.ThrowError("Pathfinding error, couldn't find a start node");
                return new SteeringPath();
            }
            
            closestDist = 0.0f;
            PathNode endNode = null;
            foreach (PathNode node in nodes)
            {
                float dist = Vector2.Distance(end, node.Position);
                if (dist < closestDist || endNode == null)
                {
                    if (insideSubmarine && Submarine.CheckVisibility(end, node.Waypoint.SimPosition) != null) continue;

                    closestDist = dist;
                    endNode = node;
                }
            }

            if (endNode == null)
            {
                DebugConsole.ThrowError("Pathfinding error, couldn't find an end node");
                return new SteeringPath();
            }


            var path =  FindPath(startNode,endNode);

            sw.Stop();
            System.Diagnostics.Debug.WriteLine("findpath: " + sw.ElapsedTicks);

            return path;
        }

        public SteeringPath FindPath(WayPoint start, WayPoint end)
        {
            PathNode startNode=null, endNode=null;
            foreach (PathNode node in nodes)
            {
                if (node.Waypoint == start)
                {
                    startNode = node;
                    if (endNode != null) break;
                }
                if (node.Waypoint == end)
                {
                    endNode = node;
                    if (startNode != null) break;
                }

                if (startNode==null || endNode==null)
                {
                    DebugConsole.ThrowError("Pathfinding error, couldn't find matching pathnodes to waypoints");
                    return new SteeringPath();
                }
            }

            return FindPath(startNode, endNode);
        }

        private SteeringPath FindPath(PathNode start, PathNode end)
        {
            if (start == end)
            {
                var path1 = new SteeringPath();
                path1.AddNode(start.Waypoint);

                return path1;
            }

            foreach (PathNode node in nodes)
            {
                node.state = 0;
                node.F = 0.0f;
                node.G = 0.0f;
                node.H = 0.0f;
            }
              
            start.state = 1;
            while (true)
            {

                PathNode currNode = null;
                float dist = 10000.0f;
                foreach (PathNode node in nodes)
                {
                    if (node.state != 1) continue;
                    if (node.F < dist)
                    {
                        dist = node.F;
                        currNode = node;
                    }
                }

                if (currNode == null || currNode == end) break;

                currNode.state = 2;

                for (int i = 0; i < currNode.connections.Count; i++)
                {
                    PathNode nextNode = currNode.connections[i];

                    //a node that hasn't been searched yet
                    if (nextNode.state==0)
                    {
                        nextNode.H = Vector2.Distance(nextNode.Position,end.Position);

                        if (GetNodePenalty != null)
                        {
                            float? nodePenalty =GetNodePenalty(currNode, nextNode);
                            if (nodePenalty == null)
                            {
                                nextNode.state = -1;
                                continue;
                            }
                            nextNode.H += (float)nodePenalty;
                        }

                        nextNode.G = currNode.G + currNode.distances[i];
                        nextNode.F = nextNode.G + nextNode.H;
                        nextNode.Parent = currNode;
                        nextNode.state = 1;
                    }
                    //node that has been searched
                    else if (nextNode.state==1)
                    {
                        float tempG = currNode.G + currNode.distances[i];
                        //only use if this new route is better than the 
                        //route the node was a part of
                        if (tempG < nextNode.G)
                        {
                            nextNode.G = tempG;
                            nextNode.F = nextNode.G + nextNode.H;
                            nextNode.Parent = currNode;
                        }
                    }
                }
            }

            if (end.state==0)
            {
                //path not found
                return new SteeringPath(true);
            }

            SteeringPath path = new SteeringPath();
            List<WayPoint> finalPath = new List<WayPoint>();

            PathNode pathNode = end;
            while (pathNode != start && pathNode != null)
            {
                finalPath.Add(pathNode.Waypoint);

                path.Cost += pathNode.F;
                pathNode = pathNode.Parent;
            }

            finalPath.Reverse();

            foreach (WayPoint wayPoint in finalPath)
            {
                path.AddNode(wayPoint);
            }
            
                
            return path;
        }
    }
}



