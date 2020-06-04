using Microsoft.Xna.Framework;
using System;
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
        public List<float> distances;
        
        public WayPoint Waypoint
        {
            get { return wayPoint; }
        }

        public Vector2 Position
        {
            get { return position; }
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
                if (wayPoint == null) { continue; }
                if (nodes.ContainsKey(wayPoint.ID))
                {
#if DEBUG
                    DebugConsole.ThrowError("Error in PathFinder.GenerateNodes (duplicate ID \"" + wayPoint.ID + "\")");
#endif
                    continue;
                }
                nodes.Add(wayPoint.ID, new PathNode(wayPoint));
            }

            foreach (KeyValuePair<int,PathNode> node in nodes)
            {
                foreach (MapEntity linked in node.Value.wayPoint.linkedTo)
                {
                    PathNode connectedNode = null;
                    nodes.TryGetValue(linked.ID, out connectedNode);
                    if (connectedNode == null) { continue; }

                    node.Value.connections.Add(connectedNode);                    
                }
            }

            var nodeList = nodes.Values.ToList();
            nodeList.RemoveAll(n => n.connections.Count == 0);
            foreach (PathNode node in nodeList)
            {
                node.distances = new List<float>();
                for (int i = 0; i< node.connections.Count; i++)
                {
                    node.distances.Add(Vector2.Distance(node.position, node.connections[i].position));
                }                
            }

            return nodeList;            
        }
    }

    class PathFinder
    {
        public delegate float? GetNodePenaltyHandler(PathNode node, PathNode prevNode);
        public GetNodePenaltyHandler GetNodePenalty;

        private List<PathNode> nodes;

        public bool InsideSubmarine { get; set; }

        public PathFinder(List<WayPoint> wayPoints, bool indoorsSteering = false)
        {
            nodes = PathNode.GenerateNodes(wayPoints.FindAll(w => w.Submarine != null == indoorsSteering));

            foreach (WayPoint wp in wayPoints)
            {
                wp.linkedTo.CollectionChanged += WaypointLinksChanged;
            }

            InsideSubmarine = indoorsSteering;
        }

        void WaypointLinksChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (Submarine.Unloading) { return; }

            var waypoints = sender as IEnumerable<MapEntity>;

            foreach (MapEntity me in waypoints)
            {
                WayPoint wp = me as WayPoint;
                if (me == null) { continue; }

                var node = nodes.Find(n => n.Waypoint == wp);
                if (node == null) { return; }

                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                {
                    for (int i = node.connections.Count - 1; i >= 0; i--)
                    {
                        //remove connection if the waypoint isn't connected anymore
                        if (wp.linkedTo.FirstOrDefault(l => l == node.connections[i].Waypoint) == null)
                        {
                            node.connections.RemoveAt(i);
                            node.distances.RemoveAt(i);
                        }
                    }
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    for (int i = 0; i < wp.linkedTo.Count; i++)
                    {
                        WayPoint connected = wp.linkedTo[i] as WayPoint;
                        if (connected == null) { continue; }

                        //already connected, continue
                        if (node.connections.Any(n => n.Waypoint == connected)) { continue; }

                        var matchingNode = nodes.Find(n => n.Waypoint == connected);
                        if (matchingNode == null)
                        {
#if DEBUG
                            DebugConsole.ThrowError("Waypoint connections were changed, no matching path node found in PathFinder");
#endif
                            return;
                        }

                        node.connections.Add(matchingNode);
                        node.distances.Add(Vector2.Distance(node.Position, matchingNode.Position));
                    }
                }
            }
        }

        public SteeringPath FindPath(Vector2 start, Vector2 end, Submarine hostSub = null, string errorMsgStr = null, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null)
        {            
            float closestDist = 0.0f;
            PathNode startNode = null;
            foreach (PathNode node in nodes)
            {
                if (nodeFilter != null && !nodeFilter(node)) { continue; }
                if (startNodeFilter != null && !startNodeFilter(node)) { continue; }
                Vector2 nodePos = node.Position;
                if (hostSub != null)
                {
                    Vector2 diff = hostSub.SimPosition - node.Waypoint.Submarine.SimPosition;
                    nodePos -= diff;
                }

                float xDiff = Math.Abs(start.X - nodePos.X);
                float yDiff = Math.Abs(start.Y - nodePos.Y);

                if (yDiff > 1.0f && node.Waypoint.Ladders == null && node.Waypoint.Stairs == null)
                {
                    yDiff += 10.0f;
                }

                float dist = xDiff + (InsideSubmarine ? yDiff * 10.0f : yDiff); //higher cost for vertical movement when inside the sub

                //prefer nodes that are closer to the end position
                dist += (Math.Abs(end.X - nodePos.X) + Math.Abs(end.Y - nodePos.Y)) / 2.0f;
                //much higher cost to waypoints that are outside
                if (node.Waypoint.CurrentHull == null && InsideSubmarine)
                {
                    dist *= 10.0f;
                }
                if (dist < closestDist || startNode == null)
                {
                    //if searching for a path inside the sub, make sure the waypoint is visible
                    if (InsideSubmarine)
                    {
                        var body = Submarine.PickBody(
                            start, nodePos, null, 
                            Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionStairs);

                        if (body != null)
                        {
                            //if (body.UserData is Submarine) continue;
                            if (body.UserData is Structure && !((Structure)body.UserData).IsPlatform) { continue; }
                            if (body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall)) { continue; }
                        }
                    }

                    closestDist = dist;
                    startNode = node;
                }
            }

            if (startNode == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Pathfinding error, couldn't find a start node. "+ errorMsgStr, Color.DarkRed);
#endif

                return new SteeringPath(true);
            }
            
            closestDist = 0.0f;
            PathNode endNode = null;
            foreach (PathNode node in nodes)
            {
                if (nodeFilter != null && !nodeFilter(node)) { continue; }
                if (endNodeFilter != null && !endNodeFilter(node)) { continue; }
                Vector2 nodePos = node.Position;
                if (hostSub != null)
                {
                    Vector2 diff = hostSub.SimPosition - node.Waypoint.Submarine.SimPosition;
                    nodePos -= diff;
                }
                float dist = Vector2.DistanceSquared(end, nodePos);
                if (InsideSubmarine)
                {
                    //much higher cost to waypoints that are outside
                    if (node.Waypoint.CurrentHull == null) { dist *= 10.0f; }
                    //avoid stopping at a doorway
                    if (node.Waypoint.ConnectedDoor != null) { dist *= 10.0f; }
                }
                if (dist < closestDist || endNode == null)
                {
                    //if searching for a path inside the sub, make sure the waypoint is visible
                    if (InsideSubmarine)
                    {
                        var body = Submarine.PickBody(end, nodePos, null,
                            Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionStairs );

                        if (body != null)
                        {
                            //if (body.UserData is Submarine) continue;
                            if (body.UserData is Structure && !((Structure)body.UserData).IsPlatform) { continue; }
                            if (body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall)) { continue; }
                        }
                    }

                    closestDist = dist;
                    endNode = node;
                }
            }

            if (endNode == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Pathfinding error, couldn't find an end node. " + errorMsgStr, Color.DarkRed);
#endif
                return new SteeringPath(true);
            }

            var path = FindPath(startNode, endNode, nodeFilter);

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
            }

            if (startNode == null || endNode == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Pathfinding error, couldn't find matching pathnodes to waypoints.", Color.DarkRed);
#endif
                return new SteeringPath(true);
            }

            return FindPath(startNode, endNode);
        }

        private SteeringPath FindPath(PathNode start, PathNode end, Func<PathNode, bool> filter = null)
        {
            if (start == end)
            {
                var path1 = new SteeringPath();
                path1.AddNode(start.Waypoint);

                return path1;
            }

            foreach (PathNode node in nodes)
            {
                node.Parent = null;
                node.state = 0;
                node.F = 0.0f;
                node.G = 0.0f;
                node.H = 0.0f;
            }

            start.state = 1;
            while (true)
            {
                PathNode currNode = null;
                float dist = float.MaxValue;
                foreach (PathNode node in nodes)
                {
                    if (filter != null && !filter(node)) { continue; }
                    if (node.state != 1) { continue; }
                    if (node.F < dist)
                    {
                        dist = node.F;
                        currNode = node;
                    }
                }

                if (currNode == null || currNode == end) { break; }

                currNode.state = 2;

                for (int i = 0; i < currNode.connections.Count; i++)
                {
                    PathNode nextNode = currNode.connections[i];
                    
                    //a node that hasn't been searched yet
                    if (nextNode.state == 0)
                    {
                        nextNode.H = Vector2.Distance(nextNode.Position, end.Position);

                        float penalty = 0.0f;
                        if (GetNodePenalty != null)
                        {
                            float? nodePenalty = GetNodePenalty(currNode, nextNode);
                            if (nodePenalty == null)
                            {
                                nextNode.state = -1;
                                continue;
                            }
                            penalty = nodePenalty.Value;
                        }

                        nextNode.G = currNode.G + currNode.distances[i] + penalty;
                        nextNode.F = nextNode.G + nextNode.H;
                        nextNode.Parent = currNode;
                        nextNode.state = 1;
                    }
                    //node that has been searched
                    else if (nextNode.state == 1 || nextNode.state == -1)
                    {
                        float tempG = currNode.G + currNode.distances[i];
                        
                        if (GetNodePenalty != null)
                        {
                            float? nodePenalty = GetNodePenalty(currNode, nextNode);
                            if (nodePenalty == null) { continue; }
                            tempG += nodePenalty.Value;
                        }

                        //only use if this new route is better than the 
                        //route the node was a part of
                        if (tempG < nextNode.G)
                        {
                            nextNode.G = tempG;
                            nextNode.F = nextNode.G + nextNode.H;
                            nextNode.Parent = currNode;
                            nextNode.state = 1;
                        }
                    }
                }
            }

            if (end.state == 0 || end.Parent == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Path not found", Color.Yellow);
#endif
                return new SteeringPath(true);
            }

            SteeringPath path = new SteeringPath();
            List<WayPoint> finalPath = new List<WayPoint>();

            PathNode pathNode = end;
            while (pathNode != start && pathNode != null)
            {
                finalPath.Add(pathNode.Waypoint);

                //(there was one bug report that seems to have been caused by this loop never terminating:
                //couldn't reproduce or figure out what caused it, but here's a workaround that prevents the game from crashing in case it happens again)

                //should be fixed now, was most likely caused by the parent fields of the nodes not being cleared before starting the pathfinding
                if (finalPath.Count > nodes.Count)
                {
#if DEBUG
                    DebugConsole.ThrowError("Pathfinding error: constructing final path failed");
#endif
                    return new SteeringPath(true);
                }

                path.Cost += pathNode.F;
                pathNode = pathNode.Parent;
            }

            finalPath.Add(start.Waypoint);

            finalPath.Reverse();

            foreach (WayPoint wayPoint in finalPath)
            {
                path.AddNode(wayPoint);
            }
            
                
            return path;
        }
    }
}



