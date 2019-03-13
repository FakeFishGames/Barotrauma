using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveIdle : AIObjective
    {
        public override string DebugTag => "idle";

        const float WallAvoidDistance = 150.0f;

        private Hull currentTarget;
        private float newTargetTimer;

        private float standStillTimer;
        private float walkDuration;

        public AIObjectiveIdle(Character character) : base(character, "")
        {
            standStillTimer = Rand.Range(-10.0f, 10.0f);
            walkDuration = Rand.Range(0.0f, 10.0f);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            return 1.0f;
        }

        protected override void Act(float deltaTime)
        {
            if (PathSteering == null) return;

            //don't keep dragging others when idling
            if (character.SelectedCharacter != null)
            {
                character.DeselectCharacter();
            }
            if (!character.IsClimbing)
            {
                character.SelectedConstruction = null;
            }

            bool currentTargetIsInvalid = currentTarget == null || IsForbidden(currentTarget) || 
                (PathSteering.CurrentPath != null && PathSteering.CurrentPath.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull)));

            if (currentTargetIsInvalid || (currentTarget == null && IsForbidden(character.CurrentHull)))
            {
                newTargetTimer = 0;
                standStillTimer = 0;
            }
            if (character.AnimController.InWater || character.IsClimbing)
            {
                standStillTimer = 0;
            }
            if (newTargetTimer <= 0.0f)
            {
                currentTarget = FindRandomHull();

                if (currentTarget != null)
                {
                    string errorMsg = null;
#if DEBUG
                    bool isRoomNameFound = currentTarget.RoomName != null;
                    errorMsg = "(Character " + character.Name + " idling, target " + (isRoomNameFound ? currentTarget.RoomName : currentTarget.ToString()) + ")";
#endif
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, currentTarget.SimPosition, errorMsg);
                    PathSteering.SetPath(path);
                }

                newTargetTimer = currentTarget == null ? 5.0f : 15.0f;
            }
            
            newTargetTimer -= deltaTime;

            //wander randomly 
            // - if reached the end of the path 
            // - if the target is unreachable
            // - if the path requires going outside
            if (PathSteering == null || (PathSteering.CurrentPath != null &&
                (PathSteering.CurrentPath.NextNode == null || PathSteering.CurrentPath.Unreachable || PathSteering.CurrentPath.HasOutdoorsNodes)))
            {
                standStillTimer -= deltaTime;
                if (standStillTimer > 0.0f)
                {
                    walkDuration = Rand.Range(1.0f, 5.0f);
                    PathSteering.Reset();
                    return;
                }

                if (standStillTimer < -walkDuration)
                {
                    standStillTimer = Rand.Range(1.0f, 10.0f);
                }

                //steer away from edges of the hull
                if (character.AnimController.CurrentHull != null && !character.IsClimbing)
                {
                    float leftDist = character.Position.X - character.AnimController.CurrentHull.Rect.X;
                    float rightDist = character.AnimController.CurrentHull.Rect.Right - character.Position.X;

                    if (leftDist < WallAvoidDistance && rightDist < WallAvoidDistance)
                    {
                        if (Math.Abs(rightDist - leftDist) > WallAvoidDistance / 2)
                        {
                            PathSteering.SteeringManual(deltaTime, Vector2.UnitX * Math.Sign(rightDist - leftDist));
                        }
                        else
                        {
                            PathSteering.Reset();
                            return;
                        }
                    }
                    else if (leftDist < WallAvoidDistance)
                    {
                        PathSteering.SteeringManual(deltaTime, Vector2.UnitX * (WallAvoidDistance-leftDist) / WallAvoidDistance);
                        PathSteering.WanderAngle = 0.0f;
                        return;
                    }
                    else if (rightDist < WallAvoidDistance)
                    {
                        PathSteering.SteeringManual(deltaTime, -Vector2.UnitX * (WallAvoidDistance-rightDist) / WallAvoidDistance);
                        PathSteering.WanderAngle = MathHelper.Pi;
                        return;
                    }
                }
                
                character.AIController.SteeringManager.SteeringWander();
                if (!character.IsClimbing && !character.AnimController.InWater)
                {
                    //reset vertical steering to prevent dropping down from platforms etc
                    character.AIController.SteeringManager.ResetY();
                }             
                return;                
            }

            if (currentTarget != null)
            {
                character.AIController.SteeringManager.SteeringSeek(currentTarget.SimPosition);
            }
        }

        private readonly List<Hull> targetHulls = new List<Hull>(20);
        private readonly List<float> hullWeights = new List<float>(20);

        private Hull FindRandomHull()
        {
            var idCard = character.Inventory.FindItemByIdentifier("idcard");
            Hull targetHull = null;
            //random chance of navigating back to the room where the character spawned
            if (Rand.Int(5) == 1 && idCard != null)
            {
                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (wp.SpawnType != SpawnType.Human || wp.CurrentHull == null) { continue; }

                    foreach (string tag in wp.IdCardTags)
                    {
                        if (idCard.HasTag(tag))
                        {
                            targetHull = wp.CurrentHull;
                        }
                    }
                }
            }
            if (targetHull == null)
            {
                targetHulls.Clear();
                hullWeights.Clear();
                foreach (var hull in Hull.hullList)
                {
                    if (HumanAIController.UnsafeHulls.Contains(hull)) { continue; }
                    if (hull.Submarine == null) { continue; }
                    if (hull.Submarine.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(hull, true)) { continue; }
                    if (IsForbidden(hull)) { continue; }
                    // Ignore hulls that are too low to stand inside
                    if (character.AnimController is HumanoidAnimController animController)
                    {
                        if (hull.CeilingHeight < ConvertUnits.ToDisplayUnits(animController.HeadPosition.Value))
                        {
                            continue;
                        }
                    }
                    // Check that there is no unsafe or forbidden hulls on the way to the target
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, hull.SimPosition);
                    if (path.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull) || IsForbidden(n.CurrentHull))) { continue; }

                    // If we want to do a steering check, we should do it here, before setting the path
                    //if (path.Cost > 1000.0f) { continue; }

                    if (!targetHulls.Contains(hull))
                    {
                        targetHulls.Add(hull);
                        hullWeights.Add(hull.Volume);
                    }
                }
                return ToolBox.SelectWeightedRandom(targetHulls, hullWeights, Rand.RandSync.Unsynced);
            }
            return targetHull;
        }

        private bool IsForbidden(Hull hull)
        {
            if (hull == null) { return true; }
            string hullName = hull.RoomName?.ToLowerInvariant();
            bool isForbidden = hullName == "ballast" || hullName == "airlock";
            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull == hull && (item.HasTag("ballast") || item.HasTag("airlock")))
                {
                    isForbidden = true;
                    break;
                }
            }
            return isForbidden;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveIdle);
        }
    }
}
