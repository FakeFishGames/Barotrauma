using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class IndoorsSteeringManager : SteeringManager
    {
        private PathFinder pathFinder;
        private SteeringPath currentPath;

        private bool canOpenDoors, canBreakDoors;

        private Character character;

        private Vector2 currentTarget;

        private float findPathTimer;

        private float buttonPressCooldown;

        const float ButtonPressInterval = 0.5f;

        public SteeringPath CurrentPath
        {
            get { return currentPath; }
        }

        public PathFinder PathFinder
        {
            get { return pathFinder; }
        }

        public Vector2 CurrentTarget
        {
            get { return currentTarget; }
        }

        public bool IsPathDirty
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns true if the current or the next node is in ladders.
        /// </summary>
        public bool InLadders =>
            currentPath != null &&
            currentPath.CurrentNode != null && (currentPath.CurrentNode.Ladders != null ||
            (currentPath.NextNode != null && currentPath.NextNode.Ladders != null));

        /// <summary>
        /// Returns true if any node in the path is in stairs
        /// </summary>
        public bool InStairs => currentPath != null && currentPath.Nodes.Any(n => n.Stairs != null);

        public bool IsNextNodeLadder
        {
            get
            {
                if (currentPath == null) { return false; }
                if (currentPath.NextNode == null) { return false; }
                if (currentPath.NextNode.Ladders != null)
                {
                    return true;
                }
                else
                {
                    // Check if the node after the next node is ladder.
                    int index = currentPath.CurrentIndex + 2;
                    if (currentPath.Nodes.Count > index)
                    {
                        var node = currentPath.Nodes[index];
                        if (node == null) { return false; }
                        return node.Ladders != null;
                    }
                    return false;
                }
            }
        }

        public bool IsNextLadderSameAsCurrent
        {
            get
            {
                if (currentPath == null) { return false; }
                if (currentPath.CurrentNode == null) { return false; }
                if (currentPath.NextNode == null) { return false; }
                var currentLadder = currentPath.CurrentNode.Ladders;
                if (currentLadder == null) { return false; }
                var nextLadder = currentPath.NextNode.Ladders;
                if (nextLadder != null)
                {
                    return currentLadder == nextLadder;
                }
                else
                {
                    // Check if the node after the next node is in the same ladder as the current.
                    int index = currentPath.CurrentIndex + 2;
                    if (currentPath.Nodes.Count > index)
                    {
                        var node = currentPath.Nodes[index];
                        if (node == null) { return false; }
                        nextLadder = node.Ladders;
                        bool isSame = nextLadder != null && nextLadder == currentLadder;
                        return isSame;
                    }
                    return false;
                }
            }
        }

        public IndoorsSteeringManager(ISteerable host, bool canOpenDoors, bool canBreakDoors) : base(host)
        {
            pathFinder = new PathFinder(WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path), true);
            pathFinder.GetNodePenalty = GetNodePenalty;

            this.canOpenDoors = canOpenDoors;
            this.canBreakDoors = canBreakDoors;

            character = (host as AIController).Character;

            findPathTimer = Rand.Range(0.0f, 1.0f);
        }

        public override void Update(float speed)
        {
            base.Update(speed);

            buttonPressCooldown -= 1.0f / 60.0f;
            findPathTimer -= 1.0f / 60.0f;
        }

        public void SetPath(SteeringPath path)
        {
            currentPath = path;
            if (path.Nodes.Any()) currentTarget = path.Nodes[path.Nodes.Count - 1].SimPosition;
            findPathTimer = 1.0f;
            IsPathDirty = false;
        }

        public void SteeringSeek(Vector2 target, float weight, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null)
        {
            steering += CalculateSteeringSeek(target, weight, startNodeFilter, endNodeFilter, nodeFilter);
        }

        private Vector2 CalculateSteeringSeek(Vector2 target, float weight, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null)
        {
            bool needsNewPath = currentPath == null || (currentPath.Unreachable || currentPath.NextNode == null) || Vector2.DistanceSquared(target, currentTarget) > 1;
            //find a new path if one hasn't been found yet or the target is different from the current target
            if (needsNewPath || findPathTimer < -1.0f)
            {
                IsPathDirty = true;
                if (findPathTimer > 0.0f) { return Vector2.Zero; }
                currentTarget = target;
                var newPath = pathFinder.FindPath(host.SimPosition, target, character.Submarine, "(Character: " + character.Name + ")", startNodeFilter, endNodeFilter, nodeFilter);
                bool useNewPath = currentPath == null || needsNewPath || currentPath.Finished;
                if (!useNewPath && currentPath != null && currentPath.CurrentNode != null && newPath.Nodes.Any() && !newPath.Unreachable)
                {
                    // It's possible that the current path was calculated from a start point that is no longer valid.
                    // Therefore, let's accept also paths with a greater cost than the current, if the current node is much farther than the new start node.
                    useNewPath = newPath.Cost < currentPath.Cost ||
                        Vector2.DistanceSquared(character.WorldPosition, currentPath.CurrentNode.WorldPosition) > Math.Pow(Vector2.Distance(character.WorldPosition, newPath.Nodes.First().WorldPosition) * 2, 2);
                }
                if (useNewPath)
                {
                    currentPath = newPath;
                }
                findPathTimer = Rand.Range(1.0f, 1.2f);
                IsPathDirty = false;
                return DiffToCurrentNode();
            }

            Vector2 diff = DiffToCurrentNode();
            var collider = character.AnimController.Collider;
            //if not in water and the waypoint is between the top and bottom of the collider, no need to move vertically
            if (!character.AnimController.InWater && !character.IsClimbing && diff.Y < collider.height / 2 + collider.radius)
            {
                diff.Y = 0.0f;
            }
            if (diff.LengthSquared() < 0.001f) { return -host.Steering; }
            return Vector2.Normalize(diff) * weight;
        }

        protected override Vector2 DoSteeringSeek(Vector2 target, float weight) => CalculateSteeringSeek(target, weight, null, null, null);

        private Vector2 DiffToCurrentNode()
        {
            if (currentPath == null || currentPath.Unreachable) return Vector2.Zero;

            if (currentPath.Finished)
            {
                Vector2 pos2 = host.SimPosition;
                if (character != null && character.Submarine == null &&
                    CurrentPath.Nodes.Count > 0 && CurrentPath.Nodes.Last().Submarine != null)
                {
                    pos2 -= CurrentPath.Nodes.Last().Submarine.SimPosition;
                }
                return currentTarget - pos2;
            }
            
            if (canOpenDoors && !character.LockHands && buttonPressCooldown <= 0.0f)
            {
                CheckDoorsInPath();
            }
            
            Vector2 pos = host.SimPosition;

            if (character != null && currentPath.CurrentNode != null)
            {
                if (CurrentPath.CurrentNode.Submarine != null)
                {
                    if (character.Submarine == null)
                    {
                        pos -= CurrentPath.CurrentNode.Submarine.SimPosition;
                    }
                    else if (character.Submarine != currentPath.CurrentNode.Submarine)
                    {
                        pos -= FarseerPhysics.ConvertUnits.ToSimUnits(currentPath.CurrentNode.Submarine.Position - character.Submarine.Position);
                    }
                }
            }

            bool isDiving = character.AnimController.InWater && character.AnimController.HeadInWater;

            //only humanoids can climb ladders
            if (!isDiving && character.AnimController is HumanoidAnimController && IsNextLadderSameAsCurrent)
            {
                if (character.SelectedConstruction != currentPath.CurrentNode.Ladders.Item &&
                    currentPath.CurrentNode.Ladders.Item.IsInsideTrigger(character.WorldPosition))
                {
                    currentPath.CurrentNode.Ladders.Item.TryInteract(character, false, true);
                }
            }
            
            var collider = character.AnimController.Collider;
            if (character.IsClimbing && !isDiving)
            {
                Vector2 diff = currentPath.CurrentNode.SimPosition - pos;
                bool nextLadderSameAsCurrent = IsNextLadderSameAsCurrent;

                if (nextLadderSameAsCurrent)
                {
                    //climbing ladders -> don't move horizontally
                    diff.X = 0.0f;
                }

                //at the same height as the waypoint
                if (Math.Abs(collider.SimPosition.Y - currentPath.CurrentNode.SimPosition.Y) < (collider.height / 2 + collider.radius) * 1.25f)
                {
                    float heightFromFloor = character.AnimController.GetColliderBottom().Y - character.AnimController.FloorY;
                    if (heightFromFloor <= 0.0f)
                    {
                        diff.Y = Math.Max(diff.Y, 1.0f);
                    }
                    // We need some margin, because if a hatch has closed, it's possible that the height from floor is slightly negative.
                    float margin = 0.1f;
                    bool aboveFloor = heightFromFloor > -margin && heightFromFloor < collider.height * 1.5f;
                    if (aboveFloor || IsNextNodeLadder)
                    {
                        if (!nextLadderSameAsCurrent)
                        {
                            character.AnimController.Anim = AnimController.Animation.None;
                        }
                        currentPath.SkipToNextNode();
                    }
                }
                else if (nextLadderSameAsCurrent)
                {
                    //if the current node is below the character and the next one is above (or vice versa)
                    //and both are on ladders, we can skip directly to the next one
                    //e.g. no point in going down to reach the starting point of a path when we could go directly to the one above
                    if (Math.Sign(currentPath.CurrentNode.WorldPosition.Y - character.WorldPosition.Y) != Math.Sign(currentPath.NextNode.WorldPosition.Y - character.WorldPosition.Y))
                    {
                        currentPath.SkipToNextNode();
                    }
                }
                return diff;
            }
            else if (character.AnimController.InWater)
            {
                // If the character is underwater, we don't need the ladders anymore
                if (character.IsClimbing && isDiving)
                {
                    character.AnimController.Anim = AnimController.Animation.None;
                    character.SelectedConstruction = null;
                }
                float multiplier = MathHelper.Lerp(1, 10, MathHelper.Clamp(collider.LinearVelocity.Length() / 10, 0, 1));
                if (Vector2.DistanceSquared(pos, currentPath.CurrentNode.SimPosition) < MathUtils.Pow(collider.radius * 2 * multiplier, 2))
                {
                    currentPath.SkipToNextNode();
                }
            }
            else
            {
                Vector2 colliderBottom = character.AnimController.GetColliderBottom();
                Vector2 colliderSize = collider.GetSize();
                Vector2 velocity = collider.LinearVelocity;
                // If the character is smaller than this, it fails to use the waypoint nodes, because they are always too high.
                float minHeight = 1;
                // Cannot use the head position, because not all characters have head or it can be below the total height of the character
                float characterHeight = Math.Max(colliderSize.Y + character.AnimController.ColliderHeightFromFloor, minHeight);
                float horizontalDistance = Math.Abs(collider.SimPosition.X - currentPath.CurrentNode.SimPosition.X);
                bool isAboveFeet = currentPath.CurrentNode.SimPosition.Y > colliderBottom.Y;
                bool isNotTooHigh = currentPath.CurrentNode.SimPosition.Y < colliderBottom.Y + characterHeight;
                float margin = MathHelper.Lerp(1, 10, MathHelper.Clamp(Math.Abs(velocity.X) / 10, 0, 1));
                float targetDistance = collider.radius * margin;
                if (horizontalDistance < targetDistance && isAboveFeet && isNotTooHigh)
                {
                    currentPath.SkipToNextNode();
                }
            }

            if (currentPath.CurrentNode == null) return Vector2.Zero;

            return currentPath.CurrentNode.SimPosition - pos;
        }

        private bool CanAccessDoor(Door door, Func<Controller, bool> buttonFilter = null)
        {
            if (door.IsOpen) { return true; }
            if (canBreakDoors) { return true; }
            if (door.IsStuck) { return false; }
            if (!canOpenDoors || character.LockHands) { return false; }
            if (door.HasIntegratedButtons)
            {
                return door.HasRequiredItems(character, false);
            }
            else
            {
                return door.Item.GetConnectedComponents<Controller>(true).Any(b => b.HasRequiredItems(character, false) && (buttonFilter == null || buttonFilter(b)));
            }
        }

        private void CheckDoorsInPath()
        {
            for (int i = 0; i < 2; i++)
            {
                WayPoint currentWaypoint = null;
                WayPoint nextWaypoint = null;
                Door door = null;
                bool shouldBeOpen = false;

                if (currentPath.Nodes.Count == 1)
                {
                    door = currentPath.Nodes.First().ConnectedDoor;
                    shouldBeOpen = door != null;
                }
                else
                {
                    if (i == 0)
                    {
                        currentWaypoint = currentPath.CurrentNode;
                        nextWaypoint = currentPath.NextNode;
                    }
                    else
                    {
                        currentWaypoint = currentPath.PrevNode;
                        nextWaypoint = currentPath.CurrentNode;
                    }
                    if (currentWaypoint?.ConnectedDoor == null) { continue; }

                    if (nextWaypoint == null)
                    {
                        //the node we're heading towards is the last one in the path, and at a door
                        //the door needs to be open for the character to reach the node
                        shouldBeOpen = true;
                    }
                    else
                    {
                        door = currentWaypoint.ConnectedGap.ConnectedDoor;
                        if (door.LinkedGap.IsHorizontal)
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.X - door.Item.WorldPosition.X);
                            shouldBeOpen = (door.Item.WorldPosition.X - character.WorldPosition.X) * dir > -50.0f;
                        }
                        else
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.Y - door.Item.WorldPosition.Y);
                            shouldBeOpen = (door.Item.WorldPosition.Y - character.WorldPosition.Y) * dir > -80.0f;
                        }
                    }
                }

                if (door == null) { return; }
                
                //toggle the door if it's the previous node and open, or if it's current node and closed
                if (door.IsOpen != shouldBeOpen)
                {
                    Controller closestButton = null;
                    float closestDist = 0;
                    bool canAccess = CanAccessDoor(door, button =>
                    {
                        if (currentWaypoint == null) { return true; }
                        // Check that the button is on the right side of the door.
                        if (door.LinkedGap.IsHorizontal)
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.X - door.Item.WorldPosition.X);
                            if (button.Item.WorldPosition.X * dir > door.Item.WorldPosition.X * dir) { return false; }
                        }
                        else
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.Y - door.Item.WorldPosition.Y);
                            if (button.Item.WorldPosition.Y * dir > door.Item.WorldPosition.Y * dir) { return false; }
                        }
                        float distance = Vector2.DistanceSquared(button.Item.WorldPosition, character.WorldPosition);
                        if (closestButton == null || distance < closestDist)
                        {
                            closestButton = button;
                            closestDist = distance;
                        }
                        return true;
                    });
                    if (canAccess)
                    {
                        if (door.HasIntegratedButtons)
                        {
                            door.Item.TryInteract(character, false, true);
                            buttonPressCooldown = ButtonPressInterval;
                            break;
                        }
                        else if (closestButton != null)
                        {
                            if (Vector2.DistanceSquared(closestButton.Item.WorldPosition, character.WorldPosition) < MathUtils.Pow(closestButton.Item.InteractDistance * 2, 2))
                            {
                                closestButton.Item.TryInteract(character, false, true);
                                buttonPressCooldown = ButtonPressInterval;
                                break;
                            }
                            else
                            {
                                // Can't reach the button closest to the character.
                                // It's possible that we could reach another buttons.
                                // If this becomes an issue, we could go through them here and check if any of them are reachable
                                // (would have to cache a collection of buttons instead of a single reference in the CanAccess filter method above)
                                var body = Submarine.PickBody(character.SimPosition, character.GetRelativeSimPosition(closestButton.Item), collisionCategory: Physics.CollisionWall | Physics.CollisionLevel);
                                if (body != null)
                                {
                                    if (body.UserData is Item item)
                                    {
                                        var d = item.GetComponent<Door>();
                                        if (d == null || d.IsOpen) { return; }
                                    }
                                    // The button is on the wrong side of the door or a wall
                                    currentPath.Unreachable = true;
                                }
                                return;
                            }
                        }
                    }
                    else if (shouldBeOpen)
                    {
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Pathfinding error: Cannot access the door", Color.Yellow);
#endif
                        currentPath.Unreachable = true;
                        return;
                    }
                }
            }
        }

        private float? GetNodePenalty(PathNode node, PathNode nextNode)
        {
            if (character == null) { return 0.0f; }
            if (nextNode.Waypoint.isObstructed) { return null; }
            float penalty = 0.0f;
            if (nextNode.Waypoint.ConnectedGap != null && nextNode.Waypoint.ConnectedGap.Open < 0.9f)
            {
                var door = nextNode.Waypoint.ConnectedDoor;
                if (door == null)
                {
                    penalty = 100.0f;
                }
                else
                {
                    if (!CanAccessDoor(door, button =>
                        {
                            // Ignore buttons that are on the wrong side of the door
                            if (door.IsHorizontal)
                            {
                                if (Math.Sign(button.Item.WorldPosition.Y - door.Item.WorldPosition.Y) != Math.Sign(character.WorldPosition.Y - door.Item.WorldPosition.Y))
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                if (Math.Sign(button.Item.WorldPosition.X - door.Item.WorldPosition.X) != Math.Sign(character.WorldPosition.X - door.Item.WorldPosition.X))
                                {
                                    return false;
                                }
                            }
                            return true;
                        }))
                    {
                        return null;
                    }
                }
            }

            //non-humanoids can't climb up ladders
            if (!(character.AnimController is HumanoidAnimController))
            {
                if (node.Waypoint.Ladders != null && nextNode.Waypoint.Ladders != null &&
                    nextNode.Position.Y - node.Position.Y > 1.0f && //more than one sim unit to climb up
                    nextNode.Waypoint.CurrentHull != null && nextNode.Waypoint.CurrentHull.Surface < nextNode.Waypoint.Position.Y) //upper node not underwater
                {
                    return null;
                }
            }

            if (node.Waypoint != null && node.Waypoint.CurrentHull != null)
            {
                var hull = node.Waypoint.CurrentHull;

                if (hull.FireSources.Count > 0)
                {
                    foreach (FireSource fs in hull.FireSources)
                    {
                        penalty += fs.Size.X * 10.0f;
                    }
                }

                if (character.NeedsAir && hull.WaterVolume / hull.Rect.Width > 100.0f) penalty += 500.0f;
                if (character.PressureProtection < 10.0f && hull.WaterVolume > hull.Volume) penalty += 1000.0f;
            }

            return penalty;
        }
    }
    
}
