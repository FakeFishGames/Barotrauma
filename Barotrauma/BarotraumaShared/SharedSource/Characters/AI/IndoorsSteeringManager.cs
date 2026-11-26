using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using FarseerPhysics;
using System.Diagnostics;
using System.Collections.Generic;
using FarseerPhysics.Dynamics;

namespace Barotrauma
{
    public class IndoorsSteeringManager : SteeringManager
    {
        private readonly PathFinder pathFinder;
        private SteeringPath currentPath;

        private readonly bool canOpenDoors;
        public bool CanBreakDoors { get; set; }

        private bool ShouldBreakDoor(Door door) =>
            CanBreakDoors &&
            !door.Item.Indestructible && !door.Item.InvulnerableToDamage &&
            (door.Item.Submarine == null || door.Item.Submarine.TeamID != character.TeamID);

        private readonly Character character;

        /// <summary>
        /// In sim units.
        /// </summary>
        private Vector2 currentTargetPos;

        private float findPathTimer;

        private const float ButtonPressCooldown = 1;
        private const float WanderRaycastDelay = 1f;

        private Body wanderLeftBody;
        private Body wanderRightBody;

        private float wanderRaycastTimer;
        private float checkDoorsTimer;
        private float buttonPressTimer;

        public SteeringPath CurrentPath
        {
            get { return currentPath; }
        }

        public PathFinder PathFinder
        {
            get { return pathFinder; }
        }

        public bool IsPathDirty
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns true if any node in the path is on stairs
        /// </summary>
        public bool PathHasStairs => currentPath != null && currentPath.Nodes.Any(n => n.Stairs != null);

        public bool IsCurrentNodeLadder => GetCurrentLadder() != null;

        public bool IsNextNodeLadder => GetNextLadder() != null;

        public bool IsNextLadderSameAsCurrent
        {
            get
            {
                var currentLadder = GetCurrentLadder();
                if (currentLadder == null) { return false; }
                return currentLadder == GetNextLadder();
            }
        }

        public IndoorsSteeringManager(ISteerable host, bool canOpenDoors, bool canBreakDoors) : base(host)
        {
            pathFinder = new PathFinder(WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path), true)
            {
                GetNodePenalty = GetNodePenalty,
                GetSingleNodePenalty = GetSingleNodePenalty
            };

            this.canOpenDoors = canOpenDoors;
            this.CanBreakDoors = canBreakDoors;

            character = (host as AIController).Character;

            findPathTimer = Rand.Range(0.0f, 1.0f);
        }

        public override void Update(float speed)
        {
            base.Update(speed);
            float step = 1.0f / 60.0f;
            checkDoorsTimer -= step;
            wanderRaycastTimer -= step;
            if (lastDoor.door == null || !lastDoor.shouldBeOpen || lastDoor.door.IsFullyOpen)
            {
                buttonPressTimer = 0;
            }
            else
            {
                buttonPressTimer -= step;
            }
            findPathTimer -= step;
        }

        public void SetPath(Vector2 targetPos, SteeringPath path)
        {
            currentTargetPos = targetPos;
            currentPath = path;
            findPathTimer = Math.Min(findPathTimer, 1.0f);
            IsPathDirty = false;
        }

        public void ResetPath()
        {
            currentPath = null;
            IsPathDirty = true;
        }

        public void SteeringSeekSimple(Vector2 targetSimPos, float weight = 1)
        {
            steering += base.DoSteeringSeek(targetSimPos, weight);
        }

        /// <summary>
        /// The transient state while going in or out through a gap is a bit weird and needs to be accounted for separately.
        /// The ragdoll keeps Character.Submarine with hysteresis while near a gap (see Ragdoll.FindHull), but Character.CurrentHull is exact.
        /// </summary>
        public static bool IsPassingThroughGap(Character c) => c.Submarine != null && c.CurrentHull == null;

        /// <summary>
        /// Start node filter that keeps the path starting on the character's side of the hull boundary, so a node that's closer but behind the wall isn't chosen.
        /// While passing through a gap, either side is fine, otherwise the character might steer back mid-entry (and be stuck spinning on the threshold).
        /// </summary>
        public bool IsStartNodeOnCharactersSide(PathNode node) =>
            IsPassingThroughGap(character) || (node.Waypoint.CurrentHull == null) == (character.CurrentHull == null);

        /// <summary>
        /// </summary>
        /// <param name="outsideNodePenalty">Additional cost applied to outside nodes. When the character is inside an extra cost of 100 is also automatically added for outside nodes, unless the character is protected from the pressure. Used for example to prevent monsters from preferring outside nodes when they already are inside.</param>
        public void SteeringSeek(Vector2 target, float weight, float minGapWidth = 0, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null, bool checkVisiblity = true, float outsideNodePenalty = 0)
        {
            // Have to use a variable here or resetting doesn't work.
            Vector2 addition = CalculateSteeringSeek(target, weight, minGapWidth, startNodeFilter, endNodeFilter, nodeFilter, checkVisiblity, outsideNodePenalty);
            steering += addition;
        }

        public Ladder GetCurrentLadder() => GetLadder(currentPath?.CurrentNode);

        public Ladder GetNextLadder() => GetLadder(currentPath?.NextNode);

        private Ladder GetLadder(WayPoint wp)
        {
            if (wp?.Ladders?.Item is Item item && item.IsInteractable(character))
            {
                return wp.Ladders;
            }
            return null;
        }

        private Vector2 CalculateSteeringSeek(Vector2 target, float weight, float minGapSize = 0, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null, bool checkVisibility = true, float outsideNodePenalty = 0)
        {
            bool needsNewPath = currentPath == null || currentPath.Unreachable || currentPath.Finished || currentPath.IsAtEndNode || currentPath.CurrentNode == null;
            if (!needsNewPath && character.Submarine != null && character.Params.PathFinderPriority > 0.5f)
            {
                // If the target has moved, we need a new path.
                // Different subs are already taken into account before setting the target.
                // Triggers when either the target or we have changed subs, but only once (until the new path has been accepted).
                Vector2 targetDiff = target - currentTargetPos;
                if (targetDiff.LengthSquared() > 1)
                {
                    needsNewPath = true;
                }
            }
            //find a new path if one hasn't been found yet or the target is different from the current target
            if (needsNewPath || findPathTimer < -1.0f)
            {
                IsPathDirty = true;
                if (!needsNewPath && currentPath?.CurrentNode is WayPoint wp)
                {
                    if (character.Submarine != null && wp.Ladders == null && wp.ConnectedDoor == null && Math.Abs(character.AnimController.TargetMovement.Combine()) <= 0)
                    {
                        // Not moving -> need a new path.
                        needsNewPath = true;
                    }
                    if (character.Submarine == null && wp.CurrentHull != null)
                    {
                        // Current node inside, while we are outside
                        // -> Check that the current node is not too far (can happen e.g. if someone controls the character in the meanwhile)
                        float maxDist = 200;
                        if (Vector2.DistanceSquared(character.WorldPosition, wp.WorldPosition) > maxDist * maxDist)
                        {
                            needsNewPath = true;
                        }
                    }
                }
                if (findPathTimer < 0)
                {
                    SkipCurrentPathNodes();
                    currentTargetPos = target;
                    Vector2 currentPos = host.SimPosition;
                    pathFinder.InsideSubmarine = character.Submarine != null && !character.Submarine.Info.IsRuin;
                    pathFinder.ApplyPenaltyToOutsideNodes = character.Submarine != null && !character.IsProtectedFromPressure;
                    var newPath = pathFinder.FindPath(currentPos, target, character.Submarine, "(Character: " + character.Name + ")", minGapSize, 
                        startNodeFilter, endNodeFilter, nodeFilter, 
                        checkVisibility: checkVisibility, outsideNodePenalty: outsideNodePenalty, 
                        currentStairs: character.AnimController?.Stairs,
                        currentPath: currentPath,
                        startHull: character.CurrentHull);
                    bool useNewPath = needsNewPath;
                    if (!useNewPath && currentPath?.CurrentNode != null && newPath.Nodes.Any() && !newPath.Unreachable)
                    {
                        // Check if the new path is the same as the old, in which case we just ignore it and continue using the old path (or the progress would reset).
                        if (IsIdenticalPath())
                        {
                            useNewPath = false;
                        }
                        else if (!character.IsClimbing)
                        {
                            // Use the new path if it has significantly lower cost (don't change the path if it has marginally smaller cost. This reduces navigating backwards due to new path that is calculated from the node just behind us).
                            float t = (float)currentPath.CurrentIndex / (currentPath.Nodes.Count - 1);
                            useNewPath = newPath.Cost < currentPath.Cost * MathHelper.Lerp(0.95f, 0, t);
                            if (!useNewPath && character.Submarine != null)
                            {
                                // It's possible that the current path was calculated from a start point that is no longer valid.
                                // Therefore, let's accept also paths with a greater cost than the current, if the current node is much farther than the new start node.
                                // This is a special case for cases e.g. where the character falls and thus needs a new path.
                                useNewPath = Vector2.DistanceSquared(character.WorldPosition, currentPath.CurrentNode.WorldPosition) > Math.Pow(Vector2.Distance(character.WorldPosition, newPath.Nodes.First().WorldPosition) * 3, 2);
                            }
                        }
                        if (!useNewPath && !character.CanSeeTarget(currentPath.CurrentNode))
                        {
                            // If we are set to disregard the new path, ensure that we can actually see the current node of the old path,
                            // because it's possible that there's e.g. a closed door between us and the current node,
                            // and in that case we'd want to use the new path instead of the old.
                            // There's visibility checks in the pathfinder calls, so the new path should always be ok.
                            useNewPath = true;
                        }

                        bool IsIdenticalPath()
                        {
                            int nodeCount = newPath.Nodes.Count;
                            if (nodeCount == currentPath.Nodes.Count)
                            {
                                for (int i = 0; i < nodeCount - 1; i++)
                                {
                                    if (newPath.Nodes[i] != currentPath.Nodes[i])
                                    {
                                        return false;
                                    }
                                }
                                return true;
                            }
                            return false;
                        }
                    }
                    if (useNewPath)
                    {
                        if (currentPath != null)
                        {
                            CheckDoorsInPath();
                        }
                        currentPath = newPath;
                    }
                    float priority = MathHelper.Lerp(3, 1, character.Params.PathFinderPriority);
                    findPathTimer = priority * Rand.Range(1.0f, 1.2f);
                    IsPathDirty = false;

                    void SkipCurrentPathNodes()
                    {
                        if (!character.AnimController.InWater || character.Submarine != null) { return; }
                        if (CurrentPath == null || CurrentPath.Unreachable || CurrentPath.Finished) { return; }
                        if (CurrentPath.CurrentIndex < 0 || CurrentPath.CurrentIndex >= CurrentPath.Nodes.Count - 1) { return; }
                        var lastNode = CurrentPath.Nodes.Last();
                        Submarine targetSub = lastNode.Submarine;
                        if (targetSub != null)
                        {
                            float subSize = Math.Max(targetSub.Borders.Size.X, targetSub.Borders.Size.Y) / 2;
                            float margin = 500;
                            if (Vector2.DistanceSquared(character.WorldPosition, targetSub.WorldPosition) < MathUtils.Pow2(subSize + margin))
                            {
                                // Don't skip nodes when close to the target submarine.
                                return;
                            }
                        }
                        // Check if we could skip ahead to NextNode when the character is swimming and using waypoints outside.
                        // Do this to optimize the old path before creating and evaluating a new path.
                        // In general, this is to avoid behavior where:
                        // a) the character goes back to first reach CurrentNode when the second node would be closer; or
                        // b) the character moves along the path when they could cut through open space to reduce the total distance.
                        float pathDistance = Vector2.Distance(character.WorldPosition, CurrentPath.CurrentNode.WorldPosition);
                        pathDistance += CurrentPath.GetLength(startIndex: CurrentPath.CurrentIndex);
                        for (int i = CurrentPath.Nodes.Count - 1; i > CurrentPath.CurrentIndex + 1; i--)
                        {
                            var waypoint = CurrentPath.Nodes[i];
                            float directDistance = Vector2.DistanceSquared(character.WorldPosition, waypoint.WorldPosition);
                            if (directDistance > MathUtils.Pow2(pathDistance) || !character.CanSeeTarget(waypoint))
                            {
                                pathDistance -= CurrentPath.GetLength(startIndex: i - 1, endIndex: i);
                                continue;
                            }
                            // Before accepting this waypoint, also check "shoulders" to make sure the character's
                            // full width can pass through, avoiding issues with turrets and other protrusions
                            // (and account for potentially different coordinate spaces between here and there)
                            Vector2 diff = ConvertUnits.ToSimUnits(waypoint.WorldPosition - character.WorldPosition);
                            if (!CanPassWithShoulders(character.SimPosition, character.SimPosition + diff))
                            {
                                pathDistance -= CurrentPath.GetLength(startIndex: i - 1, endIndex: i);
                                continue;
                            }
                            CurrentPath.SkipToNode(i);
                            break;
                        }
                    }

                    bool CanPassWithShoulders(Vector2 from, Vector2 to)
                    {
                        Vector2 dir = to - from;
                        if (dir.LengthSquared() < 0.0001f) { return true; }
                        dir = Vector2.Normalize(dir);
                        Vector2 perpendicular = new Vector2(-dir.Y, dir.X);
                        // Check left shoulder
                        Vector2 leftStart = from + perpendicular * ShoulderRaycastWidth;
                        Vector2 leftEnd = to + perpendicular * ShoulderRaycastWidth;
                        if (Submarine.CheckVisibility(leftStart, leftEnd) != null) { return false; }
                        // Check right shoulder
                        Vector2 rightStart = from - perpendicular * ShoulderRaycastWidth;
                        Vector2 rightEnd = to - perpendicular * ShoulderRaycastWidth;
                        if (Submarine.CheckVisibility(rightStart, rightEnd) != null) { return false; }
                        return true;
                    }
                }
            }

            Vector2 diff = GetDiffAndAdvance();
            if (diff == Vector2.Zero) { return Vector2.Zero; }
            return Vector2.Normalize(diff) * weight;
        }

        protected override Vector2 DoSteeringSeek(Vector2 target, float weight) => CalculateSteeringSeek(target, weight);
        
        /// <summary>
        /// Decides whether and when we should skip to the next node. Returns the difference to the current node (after skipping).
        /// </summary>
        private Vector2 GetDiffAndAdvance()
        {
            if (currentPath == null || currentPath.Unreachable)
            {
                return Vector2.Zero;
            }
            if (currentPath.Finished)
            {
                Vector2 hostPosition = host.SimPosition;
                if (character != null && character.Submarine == null && CurrentPath.Nodes.Count > 0 && CurrentPath.Nodes.Last().Submarine != null)
                {
                    hostPosition -= CurrentPath.Nodes.Last().Submarine.SimPosition;
                }
                return currentTargetPos - hostPosition;
            }
            bool doorsChecked = false;
            checkDoorsTimer = Math.Min(checkDoorsTimer, GetDoorCheckTime());
            if (!character.BothHandsDisabled && checkDoorsTimer <= 0.0f)
            {
                CheckDoorsInPath();
                doorsChecked = true;
            }
            if (buttonPressTimer > 0 && lastDoor.door != null && lastDoor.shouldBeOpen && !lastDoor.door.IsFullyOpen)
            {
                // We have pressed the button and are waiting for the door to open -> Hold still until we can press the button again.
                Reset();
                return Vector2.Zero;
            }
            WayPoint currentNode = currentPath.CurrentNode;
            WayPoint nextNode = currentPath.NextNode;
            Vector2 diff = currentNode.WorldPosition - host.WorldPosition;
            float horizontalDistance = Math.Abs(diff.X);
            float verticalDistance = Math.Abs(diff.Y);
            bool isDiving = character.AnimController.InWater && character.AnimController.HeadInWater;
            bool canClimb = character.CanClimb;
            Ladder currentLadder = GetCurrentLadder();
            Ladder nextLadder = GetNextLadder();
            Ladder ladders = currentLadder ?? nextLadder;
            bool useLadders = canClimb && ladders != null;
            var collider = character.AnimController.Collider;
            Vector2 colliderSize = ConvertUnits.ToDisplayUnits(collider.GetSize());
            float colliderHeight = colliderSize.Y;
            if (character.AnimController.CurrentAnimationParams is FishGroundedParams fishGrounded)
            {
                // On monsters, the main collider might be rotated, so we need to take that into account here.
                float standAngle = fishGrounded.ColliderStandAngleInRadians * character.AnimController.Dir;
                Vector2 transformedColliderSize = PhysicsBody.RotateVector(colliderSize, standAngle);
                colliderHeight = Math.Abs(transformedColliderSize.Y);
            }
            if (useLadders)
            {
                if (character.IsClimbing && Math.Abs(diff.X) - colliderSize.X > Math.Abs(diff.Y))
                {
                    // If the current node is horizontally farther from us than vertically, we don't want to keep climbing the ladders.
                    useLadders = false;
                }
                else if (!character.IsClimbing && nextNode != null && nextLadder == null)
                {
                    Vector2 diffToNextNode = nextNode.WorldPosition - host.WorldPosition;
                    if (Math.Abs(diffToNextNode.X) > Math.Abs(diffToNextNode.Y))
                    {
                        // If the next node is horizontally farther from us than vertically, we don't want to start climbing.
                        useLadders = false;
                    }
                }
                else if (isDiving && steering.Y < 1)
                {
                    // When diving, only use ladders to get upwards (towards the surface), otherwise we can just ignore them.
                    useLadders = false;
                }
            }
            if (character.IsClimbing && !useLadders)
            {
                if (currentPath.IsAtEndNode && canClimb && ladders != null)
                {
                    // Don't release the ladders when ending a path on ladders.
                    useLadders = true;
                }
                else
                {
                    character.StopClimbing();
                }
            }
            if (useLadders && character.SelectedSecondaryItem != ladders.Item)
            {
                if (character.CanInteractWith(ladders.Item))
                {
                    ladders.Item.TryInteract(character, forceSelectKey: true);
                }
                else
                {
                    // Cannot interact with the current (or next) ladder,
                    // Try to select the previous ladder, unless it's already selected, unless the previous ladder is not adjacent to the current ladder.
                    // The intention of this code is to prevent the bots from dropping from the "double ladders".
                    var previousLadders = currentPath.PrevNode?.Ladders;
                    if (previousLadders != null && previousLadders != ladders && character.SelectedSecondaryItem != previousLadders.Item &&
                        character.CanInteractWith(previousLadders.Item) && Math.Abs(previousLadders.Item.WorldPosition.X - ladders.Item.WorldPosition.X) < 5)
                    {
                        previousLadders.Item.TryInteract(character, forceSelectKey: true);
                    }
                }
            }
            if (character.IsClimbing && useLadders)
            {
                if (currentLadder == null && nextLadder != null && character.SelectedSecondaryItem == nextLadder.Item)
                {
                    // Climbing a ladder but the path is still on the node next to the ladder -> Skip the node.
                    return NextNode(!doorsChecked);
                }
                else
                {
                    bool nextLadderSameAsCurrent = currentLadder == nextLadder;
                    float distanceMargin = colliderSize.X;
                    if (currentLadder != null && nextLadder != null)
                    {
                        //climbing ladders -> don't move horizontally
                        diff.X = 0.0f;
                    }
                    if (verticalDistance < colliderHeight / 2 * 1.25f)
                    {
                        if (nextLadder != null && !nextLadderSameAsCurrent)
                        {
                            // Try to change the ladder (hatches between two submarines)
                            if (character.SelectedSecondaryItem != nextLadder.Item && character.CanInteractWith(nextLadder.Item))
                            {
                                if (nextLadder.Item.TryInteract(character, forceSelectKey: true))
                                {
                                    return NextNode(!doorsChecked);
                                }
                            }
                        }
                        bool isAboveFloor;
                        if (diff.Y < 0)
                        {
                            // When climbing down, let's use the collider bottom to prevent getting stuck at the bottom of the ladders.
                            float colliderBottom = character.AnimController.Collider.SimPosition.Y;
                            float floorY = character.AnimController.FloorY;
                            isAboveFloor = colliderBottom > floorY;
                        }
                        else
                        {
                            // When climbing up, let's use the lowest collider (feet).
                            // We need some margin, because if a hatch has closed, it's possible that the height from floor is slightly negative,
                            // when a foot is still below the platform.
                            float heightFromFloor = character.AnimController.GetHeightFromFloor();
                            isAboveFloor = heightFromFloor > -0.1f;
                        }
                        if (isAboveFloor)
                        {
                            if (verticalDistance < distanceMargin)
                            {
                                return NextNode(!doorsChecked);
                            }
                            else if (!currentPath.IsAtEndNode && (nextLadder == null || (currentLadder != null && Math.Abs(currentLadder.Item.WorldPosition.X - nextLadder.Item.WorldPosition.X) > distanceMargin)))
                            {
                                // Can't skip the node -> Release the ladders, because the next node is not on a ladder or it's horizontally too far.
                                character.StopClimbing();
                            }
                        }
                    }
                    else if (currentLadder != null && nextNode != null)
                    {
                        if (Math.Sign(currentNode.WorldPosition.Y - character.WorldPosition.Y) != Math.Sign(nextNode.WorldPosition.Y - character.WorldPosition.Y))
                        {
                            //if the current node is below the character and the next one is above (or vice versa)
                            //and both are on ladders, we can skip directly to the next one
                            //e.g. no point in going down to reach the starting point of a path when we could go directly to the one above
                            return NextNode(!doorsChecked);
                        }
                        //heading towards a ladder waypoint below the character, but the next waypoint is above it on the same ladder 
                         // -> allow skipping to that waypoint.
                         //      Otherwise the character may get stuck trying to move to a waypoint near the floor at the bottom of the ladder, failing to get close enough because they can't move any lower.
                        else if (nextLadderSameAsCurrent && diff.Y < 0 && nextNode.WorldPosition.Y > currentNode.WorldPosition.Y)
                        {
                            return NextNode(!doorsChecked);
                        }
                    }
                }
            }
            else if (character.AnimController.InWater)
            {
                // Swimming
                var door = currentNode.ConnectedDoor;
                if (door == null || door.CanBeTraversed)
                {
                    float distanceMultiplier = MathHelper.Lerp(1, 5, MathHelper.Clamp(collider.LinearVelocity.Length() / 10, 0, 1));
                    float targetDistance = Math.Max(Math.Max(colliderSize.X, colliderSize.Y) / 2 * distanceMultiplier, 0.5f);
                    float modifiedVerticalDist = verticalDistance;
                    if (character.CurrentHull != currentNode.CurrentHull)
                    {
                        modifiedVerticalDist *= 2;
                    }
                    float distance = horizontalDistance + modifiedVerticalDist;
                    if (distance < targetDistance)
                    {
                        return NextNode(!doorsChecked);
                    }
                }
            }
            else
            {
                // Walking horizontally
                Vector2 colliderBottom = character.AnimController.GetColliderBottom();
                if (character.Submarine != currentNode.Submarine)
                { 
                    colliderBottom = Submarine.GetRelativeSimPosition(colliderBottom, currentNode.Submarine, character.Submarine);
                }
                Vector2 velocity = collider.LinearVelocity;
                // If the character is very short, it would fail to use the waypoint nodes because they are always too high.
                // If the character is very thin, it would often fail to reach the waypoints, because the horizontal distance is too small.
                // Both values are based on the human size. So basically anything smaller than humans are considered as equal in size.
                float minHeight = 1.6125001f;
                float minWidth = 0.3225f;
                // Cannot use the head position, because not all characters have head or it can be below the total height of the character
                float characterHeight = Math.Max(ConvertUnits.ToSimUnits(colliderHeight) + character.AnimController.ColliderHeightFromFloor, minHeight);
                bool isTargetTooHigh = currentNode.SimPosition.Y > colliderBottom.Y + characterHeight;
                bool isTargetTooLow = currentNode.SimPosition.Y < colliderBottom.Y;
                var door = currentNode.ConnectedDoor;
                float targetDistanceMultiplier = MathHelper.Lerp(1, 10, MathHelper.Clamp(Math.Abs(velocity.X) / 5, 0, 1));
                if (currentNode.Stairs == null)
                {
                    // Only attempt dropping if the node is below the collider bottom.
                    // Using the next node position here, because the current node might be on the top of the ladder, which can be at the same level with the character or even above it.
                    bool isBelowEnough = (nextNode ?? currentNode).WorldPosition.Y < character.WorldPosition.Y - colliderHeight / 2;
                    bool drop = false;
                    if (isBelowEnough)
                    {
                        if (!canClimb)
                        {
                            // Can't climb -> check if we should drop.
                            Door nextDoor = door ?? nextNode?.ConnectedDoor;
                            if (nextDoor is Door { IsHorizontal: true, CanBeTraversed: true } openHatch)
                            {
                               bool isHatchBelowCharacter = openHatch.LinkedGap.WorldPosition.Y < character.WorldPosition.Y;
                               if (isHatchBelowCharacter)
                               {
                                   // Trying to go through an open hatch below us -> drop.
                                   drop = true;
                               }
                            }
                            else if (currentLadder != null && !isTargetTooLow && nextDoor == null)
                            {
                                // On ladders -> drop.
                                drop = true;
                            }
                        }
                    }
                    if (drop)
                    {
                        return NextNode(!doorsChecked);
                    }
                    else if (verticalDistance < colliderHeight / 2)
                    {
                        // The waypoint is between the top and bottom of the collider, and we don't intend to drop -> no need to move vertically.
                        diff.Y = 0.0f;
                    }
                }
                else
                {
                    // On stairs
                    bool isNextNodeInSameStairs = nextNode?.Stairs == currentNode.Stairs;
                    if (!isNextNodeInSameStairs)
                    {
                        targetDistanceMultiplier = 1;
                        if (currentNode.SimPosition.Y < colliderBottom.Y + character.AnimController.ColliderHeightFromFloor * 0.25f)
                        {
                            isTargetTooLow = true;
                        }
                        Structure nextStairs = nextNode?.Stairs;
                        if (character.AnimController.Stairs != null && nextStairs != null) 
                        {
                            //currently on stairs, and the next node is not in the same stairs
                            // -> we must get off the current stairs first before we can skip to the next node, otherwise the character
                            //    would attempt to get "through the stairs" to the next ones
                            if (character.AnimController.Stairs.StairDirection == Direction.Right)
                            {
                                //the direction in which the bot should keep moving depends on the direction of the stairs and whether we're going up or down
                                diff = nextStairs.WorldPosition.Y > character.AnimController.Stairs.WorldPosition.Y ? Vector2.UnitX : -Vector2.UnitX;
                            }
                            else
                            {
                                diff = nextStairs.WorldPosition.Y > character.AnimController.Stairs.WorldPosition.Y ? -Vector2.UnitX : Vector2.UnitX;
                            }
                        }
                    }
                }
                // Walking horizontally, check whether we are close enough to the current node.
                float targetDistance = Math.Max(colliderSize.X / 2 * targetDistanceMultiplier, ConvertUnits.ToDisplayUnits(minWidth / 2));
                Debug.Assert(targetDistance < 500, "Target distance too large (a character is trying to skip on their path to a waypoint far away), something is probably off here.");
                if (!isTargetTooHigh && !isTargetTooLow && horizontalDistance < targetDistance)
                {
                    bool isBlockedByDoor = door is { CanBeTraversed: false };
                    // If both the current ladder and the next ladder are not null, we are in the middle of ladders and should let the code above handle advancing the nodes.
                    // However, if either one is null, and we get here, we are probably walking to or from ladders.
                    bool notOnLadders = currentLadder == null || nextLadder == null;
                    if (!isBlockedByDoor && notOnLadders)
                    {
                        return NextNode(!doorsChecked);
                    }
                }
            }
            return ReturnDiff();
            
            Vector2 NextNode(bool checkDoors)
            {
                if (checkDoors)
                {
                    CheckDoorsInPath();
                }
                currentPath.SkipToNextNode();
                return ReturnDiff();
            }
            
            Vector2 ReturnDiff()
            {
                if (currentPath.CurrentNode == null)
                {
                    return Vector2.Zero;
                }
                return ConvertUnits.ToSimUnits(diff);
            }
        }

        public bool CanAccessDoor(Door door, Func<Controller, bool> buttonFilter = null)
        {
            // When closing an open door, the buttonFilter still has to run below to pick the button to close it with
            if (door.CanBeTraversed && (buttonFilter == null || door.IsBroken)) { return true; }
            if (door.IsClosed)
            {
                if (!door.Item.IsInteractable(character)) { return false; }
                if (!ShouldBreakDoor(door))
                {
                    if (door.IsStuck || door.IsJammed) { return false; }
                    if (!canOpenDoors || character.BothHandsDisabled) { return false; }
                }
            }
            if (door.HasIntegratedButtons)
            {
                return door.IsOpen || door.HasAccess(character) || ShouldBreakDoor(door);
            }
            else
            {
                bool canAccessButtons = false;
                bool buttonsFound = false;
                // Check wired controllers (e.g. buttons)
                // Always run the buttonFilter delegate (inside CanAccessButton method), if defined, because it's used for find a valid controller component that can be used for closing the door, when needed.
                // TODO: connectionFilter is ignored in the recursive searches, so it does nothing here.
                foreach (Controller button in door.Item.GetConnectedComponents<Controller>(recursive: true, connectionFilter: c => c.Name is "toggle" or "set_state"))
                {
                    buttonsFound = true;
                    if (CanAccessButton(button))
                    {
                        canAccessButtons = true;
                    }
                }
                if (!canAccessButtons)
                {
                    // Check linked controllers (more complex circuits)
                    foreach (MapEntity linked in door.Item.linkedTo)
                    {
                        if (linked is not Item linkedItem) { continue; }
                        var button = linkedItem.GetComponent<Controller>();
                        if (button == null) { continue; }
                        buttonsFound = true;
                        if (CanAccessButton(button))
                        {
                            canAccessButtons = true;
                        }
                    }   
                }
                if (door.IsOpen || ShouldBreakDoor(door))
                {
                    return true;
                }
                // If no buttons were found, just trust it if we should have the access to the door. Could be there's some other mechanism controlling the door.
                return buttonsFound ? canAccessButtons : door.HasAccess(character);
                
                bool CanAccessButton(Controller button) => button.HasAccess(character) && (buttonFilter == null || buttonFilter(button));
            }
        }

        private float GetColliderLength()
        {
            Vector2 colliderSize = character.AnimController.Collider.GetSize();
            return ConvertUnits.ToDisplayUnits(Math.Max(colliderSize.X, colliderSize.Y));
        }

        private (Door door, bool shouldBeOpen) lastDoor;
        private float GetDoorCheckTime()
        {
            if (steering.LengthSquared() > 0)
            {
                return character.AnimController.IsMovingFast ? 0.1f : 0.3f;
            }
            else
            {
                return float.PositiveInfinity;
            }
        }

        private void CheckDoorsInPath()
        {
            checkDoorsTimer = GetDoorCheckTime();
            if (!canOpenDoors) { return; }
            //handle opening doors at the current waypoint, and closing them once the bot is far enough through the door to not close it on themselves
            for (int i = 0; i < 5; i++)
            {
                WayPoint currentWaypoint = null;
                WayPoint nextWaypoint = null;
                Door door = null;
                bool shouldBeOpen = false;
                //if the door is at the only node in the path, or next node, we can just open it
                Door nextDoorOnPath = currentPath.NextNode?.ConnectedDoor;
                if (currentPath.Nodes.Count == 1 || nextDoorOnPath != null)
                {
                    door = currentPath.Nodes.Count == 1
                        ? currentPath.Nodes[0].ConnectedDoor
                        : nextDoorOnPath;
                    shouldBeOpen = door != null;
                    if (i > 0) { break; }
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
                        int previousIndex = currentPath.CurrentIndex - i;
                        if (previousIndex < 0) { break; }
                        currentWaypoint = currentPath.Nodes[previousIndex];
                        nextWaypoint = currentPath.CurrentNode;
                    }
                    if (currentWaypoint?.ConnectedDoor == null) { continue; }

                    if (nextWaypoint == null)
                    {
                        //the node we're heading towards is the last one in the path, and at a door
                        //the door needs to be open for the character to reach the node
                        if (currentWaypoint.ConnectedDoor.LinkedGap is Gap linkedGap)
                        {
                            if (currentWaypoint.Submarine == null ||
                                currentWaypoint.Submarine.Info is { IsPlayer: false } ||
                                !linkedGap.IsRoomToRoom ||
                                (linkedGap.IsRoomToRoom && currentWaypoint.CurrentHull is { IsWetRoom: false }))
                            {
                                shouldBeOpen = true;
                                door = currentWaypoint.ConnectedDoor;
                            }
                        }
                    }
                    else
                    {
                        //close the door behind the bot once far enough from it
                        float colliderLength = GetColliderLength();
                        door = currentWaypoint.ConnectedDoor;
                        if (door.LinkedGap.IsHorizontal)
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.X - door.Item.WorldPosition.X);
                            float size = character.AnimController.InWater ? colliderLength : ConvertUnits.ToDisplayUnits(character.AnimController.Collider.GetSize()).X;
                            shouldBeOpen = (door.Item.WorldPosition.X - character.WorldPosition.X) * dir > -size;
                        }
                        else
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.Y - door.Item.WorldPosition.Y);
                            shouldBeOpen = (door.Item.WorldPosition.Y - character.WorldPosition.Y) * dir > -colliderLength;
                        }
                    }
                }

                if (door == null) { return; }

                if (door.BotsShouldKeepOpen) { shouldBeOpen = true; }    
                
                if ((door.IsOpen || door.IsBroken) != shouldBeOpen)
                {
                    if (!shouldBeOpen)
                    {
                        if (character.AIController is HumanAIController humanAI)
                        {
                            bool keepDoorsClosed = 
                                character.IsBot && (door.Item.Submarine != null && character.IsOnFriendlyTeam(door.Item.Submarine.TeamID)) || 
                                character.Params.AI != null && character.Params.AI.KeepDoorsClosed;
                            if (!keepDoorsClosed) { return; }
                            bool isInAirlock = door.Item.CurrentHull is { IsWetRoom: true } || character.CurrentHull is { IsWetRoom: true };
                            if (!isInAirlock)
                            {
                                // Don't slam the door at anyones face
                                if (Character.CharacterList.Any(c => c != character && humanAI.IsFriendly(c) && humanAI.VisibleHulls.Contains(c.CurrentHull) && !c.IsUnconscious))
                                {
                                    return;
                                }
                            }
                        }
                    }
                    Controller closestButton = null;
                    float closestDist = 0;
                    bool canAccess = CanAccessDoor(door, button =>
                    {
                        // Check that the button is on the right side of the door.
                        if (nextWaypoint != null)
                        {
                            if (door.LinkedGap.IsHorizontal)
                            {
                                int dir = Math.Sign((nextWaypoint).WorldPosition.X - door.Item.WorldPosition.X);
                                if (button.Item.WorldPosition.X * dir > door.Item.WorldPosition.X * dir) { return false; }
                            }
                            else
                            {
                                int dir = Math.Sign((nextWaypoint).WorldPosition.Y - door.Item.WorldPosition.Y);
                                if (button.Item.WorldPosition.Y * dir > door.Item.WorldPosition.Y * dir) { return false; }
                            }
                        }
                        float distance = Vector2.DistanceSquared(button.Item.WorldPosition, character.WorldPosition);
                        //heavily prefer buttons linked to the door, so sub builders can help the bots figure out which button to use by linking them
                        if (door.Item.linkedTo.Contains(button.Item)) { distance *= 0.1f; }
                        if (closestButton == null || distance < closestDist)
                        {
                            if (distance < MathUtils.Pow2(button.Item.InteractDistance + GetColliderLength()) && character.CanSeeTarget(button.Item))
                            {
                                closestButton = button;
                                closestDist = distance;
                            }
                        }
                        return closestButton != null;
                    });
                    if (canAccess)
                    {
                        bool pressButton = buttonPressTimer <= 0 || lastDoor.door != door || lastDoor.shouldBeOpen != shouldBeOpen;
                        if (door.HasIntegratedButtons)
                        {
                            if (pressButton && character.CanInteractWith(door.Item))
                            {
                                if (door.Item.TryInteract(character, forceSelectKey: true))
                                {
                                    lastDoor = (door, shouldBeOpen);
                                    buttonPressTimer = shouldBeOpen ? ButtonPressCooldown : 0;
                                }
                                else
                                {
                                    buttonPressTimer = 0;
                                }
                            }
                            break;
                        }
                        else if (closestButton != null)
                        {
                            if (pressButton)
                            {
                                if (closestButton.Item.TryInteract(character, forceSelectKey: true))
                                {
                                    lastDoor = (door, shouldBeOpen);
                                    buttonPressTimer = shouldBeOpen ? ButtonPressCooldown : 0;
                                }
                                else
                                {
                                    buttonPressTimer = 0;
                                }
                            }
                            break;
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
            float? penalty = GetSingleNodePenalty(nextNode);
            if (penalty == null) { return null; }
            Vector2 nextNodePosition = nextNode.Position;
            if (nextNode.Waypoint.Submarine != node.Waypoint.Submarine)
            {
                nextNodePosition = Submarine.GetRelativeSimPosition(nextNodePosition, node.Waypoint.Submarine, nextNode.Waypoint.Submarine);
            }
            bool nextNodeAboveWaterLevel = nextNode.Waypoint.CurrentHull != null && nextNode.Waypoint.CurrentHull.Surface < nextNode.Waypoint.Position.Y;
            if (!character.CanClimb && node.Waypoint.Stairs == null && nextNode.Waypoint.Stairs == null)
            {
                if (node.Waypoint.Ladders != null && nextNode.Waypoint.Ladders != null && (!nextNode.Waypoint.Ladders.Item.IsInteractable(character) || character.BothHandsDisabled) ||
                    (nextNodePosition.Y - node.Position.Y > 1.0f && //more than one sim unit to climb up
                     nextNodeAboveWaterLevel)) //upper node not underwater
                {
                    return null;
                }
            }

            if (node.Waypoint.CurrentHull != null)
            {
                var hull = node.Waypoint.CurrentHull;
                if (hull.FireSources.Count > 0)
                {
                    foreach (FireSource fs in hull.FireSources)
                    {
                        penalty += fs.Size.X * 10.0f;
                    }
                }
                if (character.NeedsAir)
                {
                    if (hull.WaterVolume / hull.Rect.Width > 100.0f)
                    {
                        if (!HumanAIController.HasDivingSuit(character) && character.CharacterHealth.OxygenLowResistance < 1)
                        {
                            penalty += 500.0f;
                        }
                    }
                    if (character.PressureProtection < 10.0f && hull.WaterVolume > hull.Volume)
                    {
                        penalty += 1000.0f;
                    }
                }

                float yDist = Math.Abs(node.Position.Y - nextNodePosition.Y);
                if (nextNodeAboveWaterLevel && node.Waypoint.Ladders == null && nextNode.Waypoint.Ladders == null && node.Waypoint.Stairs == null && nextNode.Waypoint.Stairs == null)
                {
                    penalty += yDist * 10.0f;
                }
            }

            return penalty;
        }

        private float? GetSingleNodePenalty(PathNode node)
        {
            if (!node.Waypoint.IsTraversable) { return null; }
            if (node.IsBlocked()) { return null; }
            float penalty = 0.0f;
            if (node.Waypoint.ConnectedGap is { Open: < 0.9f })
            {
                var door = node.Waypoint.ConnectedDoor;
                if (door == null)
                {
                    penalty = 100.0f;
                }
                else
                {
                    if (!CanAccessDoor(door, button =>
                    {
                        if (Vector2.DistanceSquared(door.Item.WorldPosition, button.Item.WorldPosition) > MathUtils.Pow2(button.Item.InteractDistance + GetColliderLength()))
                        {
                            // Too far from the door.
                            return false;
                        }
                        if (!ISpatialEntity.IsTargetVisible(button.Item, door.Item))
                        {
                            // Obstructed.
                            return false;
                        }
                        // Ignore buttons that are on the wrong side of the door, unless there's a motion sensor connected to the door, which can be triggered by the character.
                        if (door.IsHorizontal)
                        {
                            if (Math.Sign(button.Item.WorldPosition.Y - door.Item.WorldPosition.Y) != Math.Sign(character.WorldPosition.Y - door.Item.WorldPosition.Y))
                            {
                                return door.Item.GetDirectlyConnectedComponent<MotionSensor>() is MotionSensor ms && ms.TriggersOn(character);
                            }
                        }
                        else
                        {
                            if (Math.Sign(button.Item.WorldPosition.X - door.Item.WorldPosition.X) != Math.Sign(character.WorldPosition.X - door.Item.WorldPosition.X))
                            {
                                return door.Item.GetDirectlyConnectedComponent<MotionSensor>() is MotionSensor ms && ms.TriggersOn(character);
                            }
                        }
                        return true;
                    }))
                    {
                        return null;
                    }
                }
            }
            return penalty;
        }

        // TODO: Long and complex. Consider refactoring.
        public static float smallRoomSize = 400;
        public void Wander(float deltaTime, float wallAvoidDistance = 150, bool stayStillInTightSpace = true)
        {
            //steer away from edges of the hull
            bool wander = false;
            bool inWater = character.AnimController.InWater;

            //use the hull the legs are in (if one is found), so the character won't walk against the wall when their torso is in a different hull where there'd be room to walk further
            //(e.g. if the character is in a shallow pool-type room, like in ResearchModule_01_Colony)
            Hull currentHull = 
                character.AnimController.GetLimb(LimbType.RightLeg)?.Hull ??
                character.AnimController.GetLimb(LimbType.LeftLeg)?.Hull ??
                character.CurrentHull;
           
            if (currentHull != null && !inWater)
            {
                float roomWidth = currentHull.Rect.Width;
                if (stayStillInTightSpace && roomWidth < Math.Max(wallAvoidDistance * 3, smallRoomSize))
                {
                    Reset();
                }
                else
                {
                    bool isVerySmallRoom = roomWidth < smallRoomSize;
                    Hull nextRoom = null;

                    if (!stayStillInTightSpace && isVerySmallRoom)
                    {
                        float closestDistance = 0;
                        // Try to steer to the next room
                        foreach (Gap gap in currentHull.ConnectedGaps)
                        {
                            if (gap.Open < 1.0f) { continue; }
                            float feetPos = ConvertUnits.ToDisplayUnits(character.AnimController.FloorY);
                            float gapTop = gap.Rect.Y;
                            float gapBottom = gap.Rect.Y - gap.Rect.Height;
                            const float margin = 25;
                            if (character.Position.Y > gapTop || feetPos < gapBottom - margin)
                            {
                                // If the character is above or below the gap, they can't walk through it.
                                continue;
                            }
                            Hull room = null;
                            foreach (var entity in gap.linkedTo)
                            {
                                if (entity is not Hull hull) { continue; }
                                if (hull.Submarine != character.Submarine) { continue; }
                                if (hull.Rect.Width < smallRoomSize) { continue; }
                                if (hull != currentHull)
                                {
                                    room = hull;
                                    break;
                                }
                            }
                            if (room == null) { continue; }
                            Vector2 toGap = gap.Position - character.Position;
                            float distance = Math.Abs(toGap.X);
                            if (nextRoom == null || distance < closestDistance)
                            {
                                closestDistance = distance;
                                nextRoom = room;
                            }
                        }
                    }
                    if (nextRoom != null)
                    {
                        float toNextRoom = nextRoom.Position.X - character.Position.X;
                        SteeringManual(deltaTime, Vector2.UnitX * Math.Sign(toNextRoom));
                        WanderAngle = 0.0f;
                    }
                    else
                    {
                        if (!stayStillInTightSpace && isVerySmallRoom)
                        {
                            // Reset regardless, because moving inside the room would look glitchy.
                            Reset();
                            return;
                        }

                        float leftDist = character.Position.X - currentHull.Rect.X;
                        float rightDist = currentHull.Rect.Right - character.Position.X;

                        if (wanderRaycastTimer <= 0)
                        {

                            // Check the left and right side and adjust the distance if there's something blocking the way
                            Vector2 raycastDist = new Vector2(10.0f, 0.0f); //10 meters
                            wanderLeftBody = Submarine.PickBody(character.SimPosition, character.SimPosition - raycastDist, collisionCategory: Physics.CollisionWall | Physics.CollisionItemBlocking);
                            wanderRightBody = Submarine.PickBody(character.SimPosition, character.SimPosition + raycastDist, collisionCategory: Physics.CollisionWall | Physics.CollisionItemBlocking);
                            wanderRaycastTimer = WanderRaycastDelay;
                        }

                        if (wanderLeftBody != null)
                        {
                            leftDist = ConvertUnits.ToDisplayUnits(character.SimPosition.X - wanderLeftBody.Position.X);
                        }

                        if (wanderRightBody != null)
                        {
                            rightDist = ConvertUnits.ToDisplayUnits(wanderRightBody.Position.X - character.SimPosition.X);
                        }

                        if (leftDist < wallAvoidDistance && rightDist < wallAvoidDistance)
                        {
                            float diff = rightDist - leftDist;
                            if (Math.Abs(diff) > wallAvoidDistance / 2)
                            {
                                SteeringManual(deltaTime, Vector2.UnitX * Math.Sign(diff));
                                return;
                            }
                            else if (stayStillInTightSpace)
                            {
                                Reset();
                                return;
                            }
                        }
                        if (leftDist < wallAvoidDistance)
                        {
                            float speed = (wallAvoidDistance - leftDist) / wallAvoidDistance;
                            SteeringManual(deltaTime, Vector2.UnitX * MathHelper.Clamp(speed, 0.25f, 1));
                            WanderAngle = 0.0f;
                        }
                        else if (rightDist < wallAvoidDistance)
                        {
                            float speed = (wallAvoidDistance - rightDist) / wallAvoidDistance;
                            SteeringManual(deltaTime, -Vector2.UnitX * MathHelper.Clamp(speed, 0.25f, 1));
                            WanderAngle = MathHelper.Pi;
                        }
                        else
                        {
                            wander = true;
                        }
                    }
                }
            }
            else
            {
                wander = true;
            }
            if (wander)
            {
                SteeringWander();
                if (inWater)
                {
                    SteeringAvoid(deltaTime, lookAheadDistance: ConvertUnits.ToSimUnits(wallAvoidDistance), 5);
                }
            }
            if (!inWater)
            {
                //reset vertical steering to prevent dropping down from platforms etc
                ResetY();
            }
        }
    }  
}
