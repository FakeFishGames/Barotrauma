using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class PathNode
    {
        public int state;

        public PathNode Parent;

        public float F, G, H;

        public readonly List<PathNode> connections = new List<PathNode>();
        public List<float> distances;

        public Vector2 TempPosition;
        public float TempDistance;

        public readonly WayPoint Waypoint;
        public readonly Vector2 Position;
        public readonly int WayPointID;

        public override string ToString()
        {
            return $"PathNode {WayPointID}";
        }

        public PathNode(WayPoint wayPoint)
        {
            Waypoint = wayPoint;
            Position = wayPoint.SimPosition;
            WayPointID = Waypoint.ID;
        }

        public static List<PathNode> GenerateNodes(List<WayPoint> wayPoints, bool removeOrphans)
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
            if (removeOrphans)
            {
                nodeList.RemoveAll(n => n.connections.Count == 0);
            }
            foreach (PathNode node in nodeList)
            {
                node.distances = new List<float>();
                for (int i = 0; i < node.connections.Count; i++)
                {
                    node.distances.Add(Vector2.Distance(node.Position, node.connections[i].Position));
                }
            }

            return nodeList;            
        }

        private bool? blocked;
        public bool IsBlocked()
        {
            if (blocked.HasValue) { return blocked.Value; }
            blocked = false;
            if (Waypoint.Submarine != null) { return blocked.Value; }
            if (Waypoint.Tunnel?.Type != Level.TunnelType.Cave) { return blocked.Value; }
            foreach (var w in Level.Loaded.ExtraWalls)
            {
                if (!w.IsPointInside(Waypoint.Position)) { continue; }
                if (w is DestructibleLevelWall d)
                {
                    blocked = !d.Destroyed;
                }
                if (blocked.Value) { break; }
            }
            return blocked.Value;
        }

        public void ResetBlocked()
        {
            blocked = null;
        }
    }

    class PathFinder
    {
        public delegate float? GetNodePenaltyHandler(PathNode node, PathNode prevNode);
        public GetNodePenaltyHandler GetNodePenalty;
        public delegate float? GetSingleNodePenaltyHandler(PathNode node);
        public GetSingleNodePenaltyHandler GetSingleNodePenalty;

        private readonly List<PathNode> nodes;
        private readonly bool isCharacter;

        public bool InsideSubmarine { get; set; }
        public bool ApplyPenaltyToOutsideNodes { get; set; }

        public PathFinder(List<WayPoint> wayPoints, bool isCharacter)
        {
            var filtered = isCharacter ? wayPoints : wayPoints.FindAll(w => w.Submarine == null);          
            nodes = PathNode.GenerateNodes(filtered, removeOrphans: true);
            foreach (WayPoint wp in wayPoints)
            {
                wp.OnLinksChanged += WaypointLinksChanged;
            }
            sortedNodes = new List<PathNode>(nodes.Count);
            this.isCharacter = isCharacter;
        }

        void WaypointLinksChanged(WayPoint wp)
        {
            if (Submarine.Unloading) { return; }

            var node = nodes.Find(n => n.Waypoint == wp);
            if (node == null) { return; }

            for (int i = node.connections.Count - 1; i >= 0; i--)
            {
                //remove connection if the waypoint isn't connected anymore
                if (wp.linkedTo.FirstOrDefault(l => l == node.connections[i].Waypoint) == null)
                {
                    node.connections.RemoveAt(i);
                    node.distances.RemoveAt(i);
                }
            }

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

        private readonly List<PathNode> sortedNodes;

        public SteeringPath FindPath(Vector2 start, Vector2 end, Submarine hostSub = null, string errorMsgStr = null, float minGapSize = 0, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null, bool checkVisibility = true)
        {
            foreach (PathNode node in nodes)
            {
                node.ResetBlocked();
            }

            // First calculate the temp positions for all nodes.
            foreach (PathNode node in nodes)
            {
                node.TempPosition = node.Position;
                var wpSub = node.Waypoint.Submarine;
                if (hostSub != null && wpSub == null)
                {
                    // inside and targeting outside
                    node.TempPosition -= hostSub.SimPosition;
                }
                else if (wpSub != null && hostSub != null && wpSub != hostSub)
                {
                    // different subs
                    node.TempPosition -= hostSub.SimPosition - wpSub.SimPosition;
                }
                else if (hostSub == null && wpSub != null)
                {
                    // Outside and targeting inside 
                    node.TempPosition += wpSub.SimPosition;
                }
            }

            //sort nodes roughly according to distance
            sortedNodes.Clear();
            PathNode startNode = null;
            foreach (PathNode node in nodes)
            {
                float xDiff = Math.Abs(start.X - node.TempPosition.X);
                float yDiff = Math.Abs(start.Y - node.TempPosition.Y);
                if (InsideSubmarine && !(node.Waypoint.Submarine?.Info?.IsRuin ?? false))
                {
                    //higher cost for vertical movement when inside the sub
                    if (yDiff > 1.0f && node.Waypoint.Ladders == null && node.Waypoint.Stairs == null)
                    {
                        yDiff += 10.0f;
                    }
                    node.TempDistance = xDiff + yDiff * 10.0f; 
                }
                else
                {
                    node.TempDistance = xDiff + yDiff;
                }

                //much higher cost to waypoints that are outside
                if (node.Waypoint.CurrentHull == null && ApplyPenaltyToOutsideNodes) { node.TempDistance *= 10.0f; }

                //optimization: node extremely far, don't try to use it as a start node
                if (node.TempDistance > (InsideSubmarine ? 100.0f : 800.0f))
                {
                    continue;
                }
                //optimization: node extremely close (< 1 m). If it's valid, choose it as the start node and skip the more exhaustive search for the closest one
                if (node.TempDistance < 1.0f)
                {
                    if (IsValidStartNode(node))
                    {
                        startNode = node;
                        break;
                    }
                }
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
            if (startNode == null)
            {
                foreach (PathNode node in sortedNodes)
                {
                    if (IsValidStartNode(node))
                    {
                        startNode = node;
                        break;
                    }
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
            PathNode endNode = null;
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
                }
                //optimization: node extremely far (> 100m / 800 m) from the end position, don't try to use it as an end node
                if (node.TempDistance > (InsideSubmarine ? 100.0f * 100.0f : 800.0f * 800.0f))
                {
                    continue;
                }
                //optimization: node extremely close (< 1 m). If it's valid, choose it as the end node and skip the more exhaustive search for the closest one
                if (node.TempDistance < 1.0f)
                {
                    if (IsValidEndNode(node))
                    {
                        endNode = node;
                        break;
                    }
                }
                int i = 0;
                while (i < sortedNodes.Count && sortedNodes[i].TempDistance < node.TempDistance)
                {
                    i++;
                }
                sortedNodes.Insert(i, node);
            }
            if (endNode == null)
            {
                //find the most suitable end node, starting from the ones closest to the end position
                foreach (PathNode node in sortedNodes)
                {
                    if (IsValidEndNode(node))
                    {
                        endNode = node;
                        break;
                    }
                }
            }
            if (endNode == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Pathfinding error, couldn't find an end node. " + errorMsgStr, Color.DarkRed);
#endif
                return new SteeringPath(true);
            }
            return FindPath(startNode, endNode, nodeFilter, errorMsgStr, minGapSize);

            bool IsValidStartNode(PathNode node) => IsValidNode(node, (isCharacter, start), startNodeFilter);

            bool IsValidEndNode(PathNode node) => IsValidNode(node, (isCharacter && checkVisibility, end), endNodeFilter);

            bool IsValidNode(PathNode node, (bool check, Vector2 start) visibilityCheck, Func<PathNode, bool> extraFilter)
            {
                if (nodeFilter != null && !nodeFilter(node)) { return false; }
                if (extraFilter != null && !extraFilter(node)) { return false; }
                if (GetSingleNodePenalty != null && GetSingleNodePenalty(node) == null) { return false; }
                if (node.Waypoint.ConnectedGap != null)
                {
                    if (!CanFitThroughGap(node.Waypoint.ConnectedGap, minGapSize)) { return false; }
                }
                if (visibilityCheck.check)
                {
                    var body = Submarine.PickBody(visibilityCheck.start, node.TempPosition,
                        collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionStairs);
                    if (body != null)
                    {
                        if (body.UserData is Submarine) { return false; }
                        if (body.UserData is Structure s && !s.IsPlatform) { return false; }
                        if (body.UserData is Voronoi2.VoronoiCell) { return false; }
                        if (body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall)) { return false; }
                    }
                }
                return true;
            }
        }

        private SteeringPath FindPath(PathNode start, PathNode end, Func<PathNode, bool> filter = null, string errorMsgStr = "", float minGapSize = 0)
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
                    if (node.state != 1 || node.F > dist) { continue; }
                    if (filter != null && !filter(node)) { continue; }
                    if (node.Waypoint.ConnectedGap != null)
                    {
                        if (!CanFitThroughGap(node.Waypoint.ConnectedGap, minGapSize)) { continue; }
                    }
                    dist = node.F;
                    currNode = node;
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
                if (errorMsgStr != null)
                {
                    DebugConsole.NewMessage("Path not found. " + errorMsgStr, Color.Yellow);
                }
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

        private bool CanFitThroughGap(Gap gap, float minWidth) => gap.IsHorizontal ? gap.RectHeight > minWidth : gap.RectWidth > minWidth;
    }
}