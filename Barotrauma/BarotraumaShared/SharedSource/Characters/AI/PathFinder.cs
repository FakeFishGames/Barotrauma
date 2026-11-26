using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class PathNode
    {
        public int state;

        public PathNode Parent;

        public float F, G, H;

        public readonly List<PathNode> connections = new List<PathNode>();

        public Vector2 TempPosition;
        public float TempDistance;

        public readonly WayPoint Waypoint;
        public Vector2 Position => Waypoint.SimPosition;
        public readonly int WayPointID;

        public override string ToString()
        {
            return $"PathNode {WayPointID}";
        }

        public PathNode(WayPoint wayPoint)
        {
            Waypoint = wayPoint;
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

            return nodeList;            
        }

        private bool? blocked;
        public bool IsBlocked()
        {
            if (blocked.HasValue) { return blocked.Value; }
            blocked = false;
            if (Waypoint.Submarine != null) { return blocked.Value; }
            if (Waypoint.Tunnel?.Type != Level.TunnelType.Cave) { return blocked.Value; }
            //if a waypoint that doesn't belong to a sub is inside a sub, it must be blocked by the sub
            if (Hull.FindHull(Waypoint.WorldPosition) is { } hull && hull.Submarine != null)
            {
                blocked = true;
                return true;
            }
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

    public class PathFinder
    {
        public delegate float? GetNodePenaltyHandler(PathNode node, PathNode prevNode);
        public GetNodePenaltyHandler GetNodePenalty;
        public delegate float? GetSingleNodePenaltyHandler(PathNode node);
        public GetSingleNodePenaltyHandler GetSingleNodePenalty;

        private readonly List<PathNode> nodes;
        private readonly List<PathNode> startNodeCandidates;
        private readonly bool isCharacter;

        public bool InsideSubmarine { get; set; }
        public bool ApplyPenaltyToOutsideNodes { get; set; }

        public PathFinder(List<WayPoint> wayPoints, bool isCharacter)
        {
            var filtered = 
                isCharacter ? 
                    //characters should be able to use all waypoints
                    wayPoints : 
                    //non-characters (in practice, subs) can only use waypoints not belonging to a sub, or the waypoints outside static submarines
                    wayPoints.FindAll(w => w.Submarine == null || (w.CurrentHull == null && w.Submarine.SubBody is { Body.BodyType: BodyType.Static }));    
            
            nodes = PathNode.GenerateNodes(filtered, removeOrphans: true);
            foreach (WayPoint wp in wayPoints)
            {
                wp.OnLinksChanged += WaypointLinksChanged;
            }
            sortedNodes = new List<PathNode>(nodes.Count);
            startNodeCandidates = new List<PathNode>(nodes.Count);
            this.isCharacter = isCharacter;
        }

        void WaypointLinksChanged(WayPoint wp)
        {
            if (Submarine.Unloading) { return; }

            PathNode node = nodes.Find(n => n.Waypoint == wp);
            if (node == null) { return; }

            for (int i = node.connections.Count - 1; i >= 0; i--)
            {
                //remove connection if the waypoint isn't connected anymore
                if (wp.linkedTo.FirstOrDefault(l => l == node.connections[i].Waypoint) == null)
                {
                    node.connections.RemoveAt(i);
                }
            }

            for (int i = 0; i < wp.linkedTo.Count; i++)
            {
                if (wp.linkedTo[i] is not WayPoint connectedWaypoint) { continue; }

                //already connected, continue
                if (node.connections.Any(n => n.Waypoint == connectedWaypoint)) { continue; }

                PathNode matchingNode = nodes.Find(n => n.Waypoint == connectedWaypoint);
                if (matchingNode == null)
                {
                    matchingNode = new PathNode(connectedWaypoint);
                    matchingNode.connections.Add(node);
                    nodes.Add(matchingNode);
                    connectedWaypoint.OnLinksChanged += WaypointLinksChanged;
                }

                node.connections.Add(matchingNode);

                System.Diagnostics.Debug.Assert(nodes.Count <= WayPoint.WayPointList.Count, "Something went wrong when generating nodes for PathFinder: more nodes than there are waypoints.");
                System.Diagnostics.Debug.Assert(nodes.All(n => !n.Waypoint.Removed), "Something went wrong when generating nodes for PathFinder: nodes present after the corresponding waypoint has been removed.");
            }
        }

        private readonly List<PathNode> sortedNodes;

        private readonly HashSet<PathNode> potentialNearbyStartNodes = [];

        /// <param name="startHull">The hull the pathfinding starts from (e.g. the character's current hull). If given, the optimization that immediately chooses a nearby start node only accepts nodes in this hull.</param>
        public SteeringPath FindPath(Vector2 start, Vector2 end, Submarine hostSub = null, string errorMsgStr = null, float minGapSize = 0,
            Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null,
            bool checkVisibility = true, float outsideNodePenalty = 0, Structure currentStairs = null, SteeringPath currentPath = null, Hull startHull = null)
        {
            foreach (PathNode node in nodes)
            {
                node.ResetBlocked();
            }

            //how high above the target waypoint needs to be for it to be considered so high it's hard to get to without ladders/stairs
            float wayPointHeightToPrioritizeLaddersAndStairs = ConvertUnits.ToSimUnits(WayPoint.WayPointHeightFromFloor * 1.5f);
            
            // When considering for a swimmer (not autopilot) swimming outside, consider waypoints outside submarines like they were level waypoints
            bool includeOutsideSubWaypoints = isCharacter && hostSub == null;
            bool IsInsideSubmarine(PathNode node) => node.Waypoint.Submarine != null && (!includeOutsideSubWaypoints || node.Waypoint.CurrentHull != null);
            bool IsInOtherSubmarine(PathNode node) => node.Waypoint.Submarine != hostSub && (!includeOutsideSubWaypoints || node.Waypoint.CurrentHull != null);

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
            startNodeCandidates.Clear();
            PathNode startNode = null;

            startNodeCandidates.AddRange(nodes);
            //if we have a current path, prioritize the nodes on it as the start node candidates (preferring nodes at the end of the path, closer to the target)
            //this works both as an optimization (we can usually find the best start node faster), and prevents the bot from
            //backtracking by choosing some other start node that happens to be closer to the bot than the next node on the path
            if (currentPath != null && !currentPath.Unreachable &&
                currentPath.CurrentIndex >= 0 && currentPath.CurrentIndex < currentPath.Nodes.Count)
            {
                for (int i = 0; i < currentPath.Nodes.Count; i++)
                {
                    var matchingNode = startNodeCandidates.FirstOrDefault(n => n.Waypoint == currentPath.Nodes[i]);
                    if (matchingNode != null)
                    {
                        startNodeCandidates.Remove(matchingNode);
                        startNodeCandidates.Insert(0, matchingNode);
                    }
                }
            }

            foreach (PathNode node in startNodeCandidates)
            {
                CalculateCostToReachStartNode(node);

                //optimization: node extremely far, don't try to use it as a start node
                if (node.TempDistance > (InsideSubmarine ? 100.0f : 800.0f))
                {
                    continue;
                }
                //optimization: node is very close. If it's valid, choose it or one of its connected nodes as the start node,
                //and skip the more exhaustive search for the closest one
                if (node.TempDistance < ConvertUnits.ToSimUnits(AIObjectiveGetItem.DefaultReach * 2))
                {
                    bool nodeInsideSubmarine = IsInsideSubmarine(node);
                    if (IsValidStartNode(node) &&
                        //don't choose outside nodes when inside or vice versa unless that's the last resort
                        InsideSubmarine == nodeInsideSubmarine &&
                        //don't take the shortcut with nodes in other hulls (e.g. a hull directly above or below),
                        //there may be a better node in the start hull, let the more exhaustive search find it

                        //NOTE: we intentionally don't check hulls linked to startHull here, it's safer that we
                        //only use this optimization on waypoints actually in the same hull, so we don't accidentally pick e.g. a waypoint higher up in some weird multi-hull room
                        (startHull == null || startHull == node.Waypoint.CurrentHull))
                    {
                        PathNode bestNode = node;
                        AddCostToEndFromStartNode(node);
                        potentialNearbyStartNodes.Clear();
                        //collect the nodes within 2 steps from the close-by node
                        foreach (var secondaryNode in node.connections)
                        {
                            potentialNearbyStartNodes.Add(secondaryNode);
                            foreach (var tertiaryNode in secondaryNode.connections)
                            {
                                potentialNearbyStartNodes.Add(tertiaryNode);
                            }
                        }
                        //go through the connected nodes too, in case one of them is even better
                        // this prevents the bot from sometimes backtracking when they decide to choose a node behind them as the starting node,
                        // even if there had been one in front of them that's closer to the target
                        foreach (var connected in potentialNearbyStartNodes)
                        {
                            if (!IsValidStartNode(connected)) { continue; }
                            CalculateCostToReachStartNode(connected);
                            AddCostToEndFromStartNode(connected);
                            if (connected.TempDistance < bestNode.TempDistance)
                            {
                                bestNode = connected;
                            }
                        }
                        startNode = bestNode;
                        break;
                    }
                }

                AddCostToEndFromStartNode(node);

                int i = 0;
                while (i < sortedNodes.Count && sortedNodes[i].TempDistance < node.TempDistance)
                {
                    i++;
                }
                sortedNodes.Insert(i, node);
            }

            //calculates a rough approximation of the cost to reach the node, and stores it in node.TempDistance
            void CalculateCostToReachStartNode(PathNode node)
            {
                float xDiff = Math.Abs(start.X - node.TempPosition.X);
                float yDiff = Math.Abs(start.Y - node.TempPosition.Y);
                //higher cost for vertical movement when inside the sub
                if (InsideSubmarine && !(node.Waypoint.Submarine?.Info?.IsRuin ?? false))
                {
                    if (yDiff > wayPointHeightToPrioritizeLaddersAndStairs && node.Waypoint.Ladders == null && node.Waypoint.Stairs == null)
                    {
                        yDiff += 10.0f;
                    }
                    //only apply the higher cost for vertical movement if it's more than a meter
                    //(small differences can be caused by non-meaningful variance in the vertical position of the waypoint, we only care about actually going up/down e.g. ladders or stairs)
                    if (Math.Abs(yDiff) > wayPointHeightToPrioritizeLaddersAndStairs)
                    {
                        yDiff *= 10.0f;
                    }
                }
                node.TempDistance = xDiff + yDiff;

                //prefer starting from a node in this sub
                if (IsInOtherSubmarine(node)) { node.TempDistance *= 10.0f; }

                //much higher cost to waypoints that are outside
                if (node.Waypoint.CurrentHull == null && ApplyPenaltyToOutsideNodes) { node.TempDistance *= 10.0f; }
            }

            //calculates a rough approximation of the cost to reach the end node from this specific start node, and adds it to node.TempDistance
            void AddCostToEndFromStartNode(PathNode node)
            {
                //prefer nodes that are closer to the end position
                node.TempDistance += (Math.Abs(end.X - node.TempPosition.X) + Math.Abs(end.Y - node.TempPosition.Y)) / 100.0f;
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
                //prefer starting from a node in this sub
                if (IsInOtherSubmarine(node)) { node.TempDistance *= 10.0f; }
                //optimization: node extremely far (> 100m / 800 m) from the end position, don't try to use it as an end node
                if (node.TempDistance > (InsideSubmarine ? 100.0f * 100.0f : 800.0f * 800.0f))
                {
                    continue;
                }
                //optimization: node extremely close (< 1 m). If it's valid, choose it as the end node and skip the more exhaustive search for the closest one
                if (node.TempDistance < 1.0f)
                {
                    bool nodeInsideSubmarine = IsInsideSubmarine(node);
                    if (IsValidEndNode(node) && 
                        //don't choose outside nodes when inside or vice versa unless that's the last resort
                        InsideSubmarine == nodeInsideSubmarine)
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
            float outsideNodeCostPenalty = outsideNodePenalty;
            if (ApplyPenaltyToOutsideNodes)
            {
                outsideNodeCostPenalty += 100;
            }
            return FindPath(startNode, endNode, nodeFilter, errorMsgStr, minGapSize, outsideNodeCostPenalty);

            bool IsValidStartNode(PathNode node) => IsValidNode(node, (isCharacter, start), startNodeFilter, currentStairs);

            bool IsValidEndNode(PathNode node) => IsValidNode(node, (isCharacter && checkVisibility, end), endNodeFilter,
                //disregard current stairs - whatever stairs the bot is currently at shouldn't matter for the end node
                currentStairs: null);

            bool IsValidNode(PathNode node, (bool check, Vector2 start) visibilityCheck, Func<PathNode, bool> extraFilter, Structure currentStairs)
            {
                if (node.Waypoint.IsDisabled) { return false; }
                if (nodeFilter != null && !nodeFilter(node)) { return false; }
                if (extraFilter != null && !extraFilter(node)) { return false; }
                if (GetSingleNodePenalty != null && GetSingleNodePenalty(node) == null) { return false; }
                //don't choose a waypoint that doesn't have unobstructed links to any other waypoint
                if (node.Waypoint.linkedTo.None(linked => linked is WayPoint wp && !node.Waypoint.ObstructedConnections.Contains(wp))) { return false; }
                if (node.Waypoint.ConnectedGap != null)
                {
                    if (!node.Waypoint.ConnectedGap.CanFitThrough(minGapSize)) { return false; }
                }
                if (visibilityCheck.check)
                {
                    var body = Submarine.PickBody(visibilityCheck.start, node.TempPosition,
                        collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionStairs);
                    if (body != null)
                    {
                        if (body.UserData is Submarine) { return false; }
                        if (body.UserData is Structure s) 
                        {
                            if (s.StairDirection != Direction.None)
                            {
                                if (currentStairs != null)
                                {
                                    //don't try to get through stairs if currently on stairs
                                    return false;
                                }
                                else
                                {
                                    //when not on stairs, it's fine to ignore them - the character can and should be allowed to walk past stairs
                                    return true;
                                }
                            }
                            if (!s.IsPlatform)
                            { 
                                return false; 
                            }                           
                        }
                        if (body.UserData is Voronoi2.VoronoiCell) { return false; }
                        if (body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall)) { return false; }
                    }
                }
                return true;
            }
        }

        private SteeringPath FindPath(PathNode start, PathNode end, Func<PathNode, bool> filter = null, string errorMsgStr = "", float minGapSize = 0f, float outsideNodePenalty = 0f)
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
                        if (!node.Waypoint.ConnectedGap.CanFitThrough(minGapSize)) { continue; }
                    }
                    dist = node.F;
                    currNode = node;
                }

                if (currNode == null || currNode == end) { break; }

                currNode.state = 2;

                for (int i = 0; i < currNode.connections.Count; i++)
                {
                    PathNode nextNode = currNode.connections[i];

                    switch (nextNode.state)
                    {
                        //a node that hasn't been searched yet
                        case 0:
                        {
                            // Note: TempPosition rather than Position (which would be local to the sub if they have one)
                            nextNode.H = Vector2.Distance(nextNode.TempPosition, end.TempPosition);
                            float cost = CalculateNodeCost();
                            if (cost < float.PositiveInfinity)
                            {
                                nextNode.G = cost;
                                nextNode.F = nextNode.G + nextNode.H;
                                nextNode.Parent = currNode;
                                nextNode.state = 1;
                            }
                            else
                            {
                                // Set searched and invalid.
                                nextNode.state = -1;
                            }
                            break;
                        }
                        //node that has been searched
                        case 1 or -1:
                        {
                            float tempG = CalculateNodeCost();
                            //only use if this new route is better than the 
                            //route the node was a part of
                            if (tempG < nextNode.G)
                            {
                                nextNode.G = tempG;
                                nextNode.F = nextNode.G + nextNode.H;
                                nextNode.Parent = currNode;
                                nextNode.state = 1;
                            }
                            break;
                        }
                    }
                    
                    float CalculateNodeCost()
                    {
                        float penalty = 0f;

                        if (currNode.Waypoint.ObstructedConnections.Contains(nextNode.Waypoint)) { return float.PositiveInfinity; }

                        if (GetNodePenalty != null)
                        {
                            float? nodePenalty = GetNodePenalty(currNode, nextNode);
                            if (nodePenalty.HasValue)
                            {
                                penalty += nodePenalty.Value;
                            }
                            else
                            {
                                return float.PositiveInfinity;
                            }
                        }
                        if (currNode.Waypoint.CurrentHull == null)
                        {
                            penalty += outsideNodePenalty;
                        }
                        // Note: TempPosition rather than Position (which would be local to the sub if they have one)
                        return currNode.G + Vector2.Distance(currNode.TempPosition, nextNode.TempPosition) + penalty;
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
    }
}
