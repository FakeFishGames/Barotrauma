using Barotrauma.Items.Components;
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
            var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;
            if (pathSteering == null) return;

            //don't keep dragging others when idling
            if (character.SelectedCharacter != null)
            {
                character.DeselectCharacter();
            }
            if (!character.IsClimbing)
            {
                character.SelectedConstruction = null;
            }

            if (currentTarget == null && (IsForbidden(character.CurrentHull) || !IsSafe(character.CurrentHull)))
            {
                newTargetTimer = 0;
                standStillTimer = 0;
            }
            if (character.AnimController.InWater)
            {
                standStillTimer = 0;
            }
            if (newTargetTimer <= 0.0f)
            {
                currentTarget = FindRandomHull();

                if (currentTarget != null)
                {
                    Vector2 pos = character.SimPosition;
                    if (character != null && character.Submarine == null) { pos -= Submarine.MainSub.SimPosition; }

                    string errorMsg = string.Empty;
#if DEBUG
                    bool isRoomNameFound = currentTarget.RoomName != null;
                    errorMsg = "(Character " + character.Name + " idling, target " + (isRoomNameFound ? currentTarget.RoomName : currentTarget.ToString()) + ")";
#endif
                    var path = pathSteering.PathFinder.FindPath(pos, currentTarget.SimPosition, errorMsg);
                    // TODO: fix, causes the character to remain still where it should move to the next room.
                    //if (path.Cost > 1000.0f && character.AnimController.CurrentHull != null) { return; }
                    pathSteering.SetPath(path);
                }

                newTargetTimer = currentTarget == null ? 5.0f : 15.0f;
            }
            
            newTargetTimer -= deltaTime;

            //wander randomly 
            // - if reached the end of the path 
            // - if the target is unreachable
            // - if the path requires going outside
            if (pathSteering == null || (pathSteering.CurrentPath != null &&
                (pathSteering.CurrentPath.NextNode == null || pathSteering.CurrentPath.Unreachable || pathSteering.CurrentPath.HasOutdoorsNodes)))
            {
                standStillTimer -= deltaTime;
                if (standStillTimer > 0.0f)
                {
                    walkDuration = Rand.Range(1.0f, 5.0f);
                    pathSteering.Reset();
                    return;
                }

                if (standStillTimer < -walkDuration)
                {
                    standStillTimer = Rand.Range(1.0f, 10.0f);
                }

                bool isClimbing = character.IsClimbing;
                //steer away from edges of the hull
                if (character.AnimController.CurrentHull != null && !isClimbing)
                {
                    float leftDist = character.Position.X - character.AnimController.CurrentHull.Rect.X;
                    float rightDist = character.AnimController.CurrentHull.Rect.Right - character.Position.X;

                    if (leftDist < WallAvoidDistance && rightDist < WallAvoidDistance)
                    {
                        if (Math.Abs(rightDist - leftDist) > WallAvoidDistance / 2)
                        {
                            pathSteering.SteeringManual(deltaTime, Vector2.UnitX * Math.Sign(rightDist - leftDist) * character.AnimController.GetCurrentSpeed(false));
                        }
                        else
                        {
                            pathSteering.Reset();
                            return;
                        }
                    }
                    else if (leftDist < WallAvoidDistance)
                    {
                        pathSteering.SteeringManual(deltaTime, Vector2.UnitX * (WallAvoidDistance-leftDist) / WallAvoidDistance * character.AnimController.GetCurrentSpeed(false));
                        pathSteering.WanderAngle = 0.0f;
                        return;
                    }
                    else if (rightDist < WallAvoidDistance)
                    {
                        pathSteering.SteeringManual(deltaTime, -Vector2.UnitX * (WallAvoidDistance-rightDist) / WallAvoidDistance * character.AnimController.GetCurrentSpeed(false));
                        pathSteering.WanderAngle = MathHelper.Pi;
                        return;
                    }
                }
                
                character.AIController.SteeringManager.SteeringWander(character.AnimController.GetCurrentSpeed(false));
                if (!isClimbing)
                {
                    //reset vertical steering to prevent dropping down from platforms etc
                    character.AIController.SteeringManager.ResetY();
                }             

                return;                
            }

            character.AIController.SteeringManager.SteeringSeek(currentTarget.SimPosition, character.AnimController.GetCurrentSpeed(false));
        }

        private readonly List<Hull> targetHulls = new List<Hull>(20);
        private readonly List<float> hullValues = new List<float>(20);

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
                hullValues.Clear();
                foreach (var hull in Hull.hullList)
                {
                    if (hull.Submarine == null) { continue; }
                    if (hull.Submarine.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(hull, true)) { continue; }
                    if (!IsSafe(hull)) { continue; }
                    // Ignore ballasts and airlocks
                    if (IsForbidden(hull)) { continue; }
                    // Ignore hulls that are too low to stand inside
                    if (character.AnimController is HumanoidAnimController animController)
                    {
                        if (hull.CeilingHeight < ConvertUnits.ToDisplayUnits(animController.HeadPosition.Value))
                        {
                            continue;
                        }
                    }
                    if (!targetHulls.Contains(hull))
                    {
                        targetHulls.Add(hull);
                        // Prefer larger hulls over smaller
                        hullValues.Add(hull.Volume);
                    }
                }
                return ToolBox.SelectWeightedRandom(targetHulls, hullValues, Rand.RandSync.Unsynced);
            }
            return targetHull;
        }

        private bool IsSafe(Hull hull) => AIObjectiveFindSafety.GetHullSafety(hull, character) > AIObjectiveFindSafety.HULL_SAFETY_THRESHOLD;

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
