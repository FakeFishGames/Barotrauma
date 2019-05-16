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
        /// Returns true if the current or the next node is in stairs.
        /// </summary>
        public bool InStairs =>
            currentPath != null &&
            currentPath.CurrentNode != null && (currentPath.CurrentNode.Stairs != null ||
            (currentPath.NextNode != null && currentPath.NextNode.Stairs != null));

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

        protected override Vector2 DoSteeringSeek(Vector2 target, float weight)
        {
            //find a new path if one hasn't been found yet or the target is different from the current target
            if (currentPath == null || Vector2.DistanceSquared(target, currentTarget) > 1.0f || findPathTimer < -1.0f)
            {
                IsPathDirty = true;

                if (findPathTimer > 0.0f) return Vector2.Zero;
                
                currentTarget = target;
                Vector2 pos = host.SimPosition;
                if (character != null && character.Submarine == null)
                {
                    var targetHull = Hull.FindHull(FarseerPhysics.ConvertUnits.ToDisplayUnits(target), null, false);
                    if (targetHull != null && targetHull.Submarine != null)
                    {
                        pos -= targetHull.Submarine.SimPosition;
                    }
                }

                currentPath = pathFinder.FindPath(pos, target, "(Character: " + character.Name + ")");

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

            if (diff.LengthSquared() < 0.001f) return -host.Steering;

            return Vector2.Normalize(diff) * weight;          
        }

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

                    bool aboveFloor = heightFromFloor > 0 && heightFromFloor < collider.height * 1.5f;
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
                if (Vector2.DistanceSquared(pos, currentPath.CurrentNode.SimPosition) < MathUtils.Pow(collider.radius * 4, 2))
                {
                    currentPath.SkipToNextNode();
                }
            }
            else
            {
                Vector2 colliderBottom = character.AnimController.GetColliderBottom();
                Vector2 colliderSize = collider.GetSize();
                // Cannot use the head position, because not all characters have head or it can be below the total height of the character
                float characterHeight = colliderSize.Y + character.AnimController.ColliderHeightFromFloor;
                float horizontalDistance = Math.Abs(collider.SimPosition.X - currentPath.CurrentNode.SimPosition.X);
                bool isAboveFeet = currentPath.CurrentNode.SimPosition.Y > colliderBottom.Y;
                bool isNotTooHigh = currentPath.CurrentNode.SimPosition.Y < colliderBottom.Y + characterHeight;
                
                if (horizontalDistance < collider.radius * 4 && isAboveFeet && isNotTooHigh)
                {
                    currentPath.SkipToNextNode();
                }
            }

            if (currentPath.CurrentNode == null) return Vector2.Zero;

            return currentPath.CurrentNode.SimPosition - pos;
        }

        public bool HasAccessToPath(SteeringPath path)
        {
            foreach (var node in path.Nodes)
            {
                var door = node.ConnectedDoor;
                if (door != null)
                {
                    if (door.IsOpen) { return true; }
                    if (door.IsStuck) { return false; }
                    if (door.HasIntegratedButtons)
                    {
                        if (!door.HasRequiredItems(character, false))
                        {
                            return false;
                        }
                        else
                        {
                            foreach (var button in door.Item.GetConnectedComponents<Controller>(true))
                            {
                                if (!button.HasRequiredItems(character, false))
                                {
                                    return false;
                                }
                                else if (Vector2.DistanceSquared(button.Item.WorldPosition, door.Item.WorldPosition) > button.Item.InteractDistance * button.Item.InteractDistance)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        private void CheckDoorsInPath()
        {
            for (int i = 0; i < 2; i++)
            {
                Door door = null;
                bool shouldBeOpen = false;

                if (currentPath.Nodes.Count == 1)
                {
                    door = currentPath.Nodes.First().ConnectedDoor;
                    shouldBeOpen = door != null;
                }
                else
                {
                    WayPoint node = null;
                    WayPoint nextNode = null;
                    if (i == 0)
                    {
                        node = currentPath.CurrentNode;
                        nextNode = currentPath.NextNode;
                    }
                    else
                    {
                        node = currentPath.PrevNode;
                        nextNode = currentPath.CurrentNode;
                    }
                    if (node?.ConnectedDoor == null) continue;

                    if (nextNode == null)
                    {
                        //the node we're heading towards is the last one in the path, and at a door
                        //the door needs to be open for the character to reach the node
                        shouldBeOpen = true;
                    }
                    else
                    {
                        door = node.ConnectedGap.ConnectedDoor;
                        if (door.LinkedGap.IsHorizontal)
                        {
                            int currentDir = Math.Sign(nextNode.WorldPosition.X - door.Item.WorldPosition.X);
                            shouldBeOpen = (door.Item.WorldPosition.X - character.WorldPosition.X) * currentDir > -50.0f;
                        }
                        else
                        {
                            int currentDir = Math.Sign(nextNode.WorldPosition.Y - door.Item.WorldPosition.Y);
                            shouldBeOpen = (door.Item.WorldPosition.Y - character.WorldPosition.Y) * currentDir > -80.0f;
                        }
                    }
                }

                if (door == null) { return; }
                
                //toggle the door if it's the previous node and open, or if it's current node and closed
                if (door.IsOpen != shouldBeOpen)
                {
                    if (door.IsStuck && shouldBeOpen)
                    {
                        currentPath.Unreachable = true;
                        return;
                    }

                    var buttons = door.Item.GetConnectedComponents<Controller>(true);
                    Controller closestButton = null;
                    float closestDist = 0.0f;

                    foreach (Controller controller in buttons)
                    {
                        float dist = Vector2.DistanceSquared(controller.Item.WorldPosition, character.WorldPosition);
                        if (dist > controller.Item.InteractDistance * controller.Item.InteractDistance * 2.0f) continue;

                        if (dist < closestDist || closestButton == null)
                        {
                            closestButton = controller;
                            closestDist = dist;
                        }
                    }

                    if (closestButton != null)
                    {
                        if (!closestButton.HasRequiredItems(character, false) && shouldBeOpen)
                        {
                            currentPath.Unreachable = true;
                            return;
                        }

                        closestButton.Item.TryInteract(character, false, true, false);
                        buttonPressCooldown = ButtonPressInterval;
                        break;
                    }
                    else
                    {
                        if (!door.HasRequiredItems(character, false) && shouldBeOpen)
                        {
                            currentPath.Unreachable = true;
                            return;
                        }

                        door.Item.TryInteract(character, false, true, true);
                        buttonPressCooldown = ButtonPressInterval;
                        break;
                    }
                }
            }
        }

        private float? GetNodePenalty(PathNode node, PathNode nextNode)
        {
            if (character == null) { return 0.0f; }
            
            float penalty = 0.0f;
            if (nextNode.Waypoint.ConnectedGap != null && nextNode.Waypoint.ConnectedGap.Open < 0.9f)
            {
                if (nextNode.Waypoint.ConnectedDoor == null)
                {
                    penalty = 100.0f;
                }
                else if (!canBreakDoors)
                {
                    //door closed and the character can't open doors -> node can't be traversed
                    if (!canOpenDoors || character.LockHands) { return null; }

                    var doorButtons = nextNode.Waypoint.ConnectedDoor.Item.GetConnectedComponents<Controller>();
                    if (!doorButtons.Any())
                    {
                        if (!nextNode.Waypoint.ConnectedDoor.HasRequiredItems(character, false)) { return null; }
                    }

                    foreach (Controller button in doorButtons)
                    {
                        if (Math.Sign(button.Item.Position.X - nextNode.Waypoint.Position.X) !=
                            Math.Sign(node.Position.X - nextNode.Position.X)) { continue; }

                        if (!button.HasRequiredItems(character, false)) { return null; }
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
