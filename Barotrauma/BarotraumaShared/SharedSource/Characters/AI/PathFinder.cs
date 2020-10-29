using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class PathNode
    {
        private readonly int wayPointID;

        public int state;

        public PathNode Parent;

        private Vector2 position;

        public float F, G, H;

        public List<PathNode> connections;
        public List<float> distances;

        public Vector2 TempPosition;
        public float TempDistance;

        public WayPoint Waypoint { get; private set; }

        public Vector2 Position
        {
            get { return position; }
        }

        public override string ToString()
        {
            return $"PathNode {wayPointID}";
        }

        public PathNode(WayPoint wayPoint)
        {
            this.Waypoint = wayPoint;
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

            foreach (KeyValuePair<int, PathNode> node in nodes)
            {
                foreach (MapEntity linked in node.Value.Waypoint.linkedTo)
                {
                    nodes.TryGetValue(linked.ID, out PathNode connectedNode);
                    if (connectedNode == null) { continue; }
                    if (!node.Value.connections.Contains(connectedNode)) { node.Value.connections.Add(connectedNode); }
                    if (!connectedNode.connections.Contains(node.Value)) { connectedNode.connections.Add(node.Value); }
                }
            }

            var nodeList = nodes.Values.ToList();
            nodeList.RemoveAll(n => n.connections.Count == 0);
            foreach (PathNode node in nodeList)
            {
                node.distances = new List<float>();
                for (int i = 0; i < node.connections.Count; i++)
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

        private readonly List<PathNode> nodes;
        public readonly bool IndoorsSteering;

        public bool InsideSubmarine { get; set; }
        public bool ApplyPenaltyToOutsideNodes { get; set; }

        public PathFinder(List<WayPoint> wayPoints, bool indoorsSteering = false)
        {
            nodes = PathNode.GenerateNodes(wayPoints.FindAll(w => w.Submarine != null == indoorsSteering));

            foreach (WayPoint wp in wayPoints)
            {
                wp.linkedTo.CollectionChanged += WaypointLinksChanged;
            }

            IndoorsSteering = indoorsSteering;
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
                        if (!(wp.linkedTo[i] is WayPoint connected)) { continue; }

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

        private static readonly List<PathNode> sortedNodes = new List<PathNode>();

        public SteeringPath FindPath(Vector2 start, Vector2 end, Submarine hostSub = null, string errorMsgStr = null, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null, bool checkVisibility = true)
        {
            //sort nodes roughly according to distance
            sortedNodes.Clear();
            foreach (PathNode node in nodes)
            {
                node.TempPosition = node.Position;
                if (hostSub != null)
                {
                    Vector2 diff = hostSub.SimPosition - node.Waypoint.Submarine.SimPosition;
                    node.TempPosition -= diff;
                }
                float xDiff = Math.Abs(start.X - node.TempPosition.X);
                float yDiff = Math.Abs(start.Y - node.TempPosition.Y);
                if (yDiff > 1.0f && node.Waypoint.Ladders == null && node.Waypoint.Stairs == null) { yDiff += 10.0f; }
                node.TempDistance = xDiff + (InsideSubmarine ? yDiff * 10.0f : yDiff); //higher cost for vertical movement when inside the sub

                //much higher cost to waypoints that are outside
                if (node.Waypoint.CurrentHull == null && ApplyPenaltyToOutsideNodes) { node.TempDistance *= 10.0f; }

                //prefer nodes that are closer to the end position
                node.TempDistance += (Math.Abs(end.X - node.TempPosition.X) + Math.Abs(end.Y - node.TempPosition.Y)) / 100.0f;

                int i = 0;
                while (i < sortedNodes.Count && sortedNodes[i].TempDistance < node.TempDistance)
                {
                    i++;
                }
                sortedNodes.Insert(i, node);
            }

            //find the most suitable start node, starting from the ones that are the closest
            PathNode startNode = null;
            foreach (PathNode node in sortedNodes)
            {
                if (startNode == null || node.TempDistance < startNode.TempDistance)
                {
                    if (nodeFilter != null && !nodeFilter(node)) { continue; }
                    if (startNodeFilter != null && !startNodeFilter(node)) { continue; }
                    //if searching for a path inside the sub, make sure the waypoint is visible
                    if (IndoorsSteering)
                    {
                        if (node.Waypoint.isObstructed) { continue; }

                        // Always check the visibility for the start node
                        var body = Submarine.PickBody(
                            start, node.TempPosition, null,
                            Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionStairs);
                        if (body != null)
                        {
                            if (body.UserData is Structure && !((Structure)body.UserData).IsPlatform) { continue; }
                            if (body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall)) { continue; }
                        }
                    }
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

            //sort nodes again, now based on distance from the end position
            sortedNodes.Clear();
            foreach (PathNode node in nodes)
            {
                node.TempDistance = Vector2.DistanceSquared(end, node.TempPosition);
                if (InsideSubmarine)
                {
                    if (ApplyPenaltyToOutsideNodes)
                    {
                        //much higher cost to waypoints that are outside
                        if (node.Waypoint.CurrentHull == null) { node.TempDistance *= 10.0f; }
                    }
                    //avoid stopping at a doorway
                    if (node.Waypoint.ConnectedDoor != null) { node.TempDistance *= 10.0f; }
                    //avoid stopping at a ladder
                    if (node.Waypoint.Ladders != null) { node.TempDistance *= 10.0f; }
                }

                int i = 0;
                while (i < sortedNodes.Count && sortedNodes[i].TempDistance < node.TempDistance)
                {
                    i++;
                }
                sortedNodes.Insert(i, node);
            }

            //find the most suitable end node, starting from the ones closest to the end position
            PathNode endNode = null;
            foreach (PathNode node in sortedNodes)
            {
                if (endNode == null || node.TempDistance < endNode.TempDistance)
                {
                    if (nodeFilter != null && !nodeFilter(node)) { continue; }
                    if (endNodeFilter != null && !endNodeFilter(node)) { continue; }
                    if (IndoorsSteering)
                    {
                        if (node.Waypoint.isObstructed) { continue; }
                        //if searching for a path inside the sub, make sure the waypoint is visible
                        if (checkVisibility)
                        {
                            // Only check the visibility for the end node when allowed (fix leaks)
                            var body = Submarine.PickBody(end, node.TempPosition, null,
                                Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionStairs);
                            if (body != null)
                            {
                                if (body.UserData is Structure && !((Structure)body.UserData).IsPlatform) { continue; }
                                if (body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall)) { continue; }
                            }
                        }
                    }
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

            var path = FindPath(startNode, endNode, nodeFilter, errorMsgStr);

            return path;
        }

        public SteeringPath FindPath(WayPoint start, WayPoint end)
        {
            PathNode startNode = null, endNode = null;
            foreach (PathNode node in nodes)
            {
                if (node.Waypoint == start)
                {
                    startNode = node;
                    if (endNode != null) { break; }
                }
                if (node.Waypoint == end)
                {
                    endNode = node;
                    if (startNode != null) { break; }
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

        private SteeringPath FindPath(PathNode start, PathNode end, Func<PathNode, bool> filter = null, string errorMsgStr = "")
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
                    if (node.state != 1) { continue; }
                    if (IndoorsSteering && node.Waypoint.isObstructed) { continue; }
                    if (filter != null && !filter(node)) { continue; }
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
                DebugConsole.NewMessage("Path not found. " + errorMsgStr, Color.Yellow);
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
            for (int i = finalPath.Count - 1; i >= 0; i--)
            {
                path.AddNode(finalPath[i]);
            }
            System.Diagnostics.Debug.Assert(finalPath.Count == path.Nodes.Count);

            return path;
        }
    }
}



