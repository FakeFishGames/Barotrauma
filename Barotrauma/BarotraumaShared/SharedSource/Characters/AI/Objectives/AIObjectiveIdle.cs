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
        public override bool UnequipItems => true;
        public override bool AllowOutsideSubmarine => true;

        private readonly float newTargetIntervalMin = 10;
        private readonly float newTargetIntervalMax = 20;
        private readonly float standStillMin = 2;
        private readonly float standStillMax = 10;
        private readonly float walkDurationMin = 5;
        private readonly float walkDurationMax = 10;

        private Hull currentTarget;
        private float newTargetTimer;

        private bool searchingNewHull;

        private float standStillTimer;
        private float walkDuration;

        private readonly List<Hull> targetHulls = new List<Hull>(20);
        private readonly List<float> hullWeights = new List<float>(20);

        public AIObjectiveIdle(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            standStillTimer = Rand.Range(-10.0f, 10.0f);
            walkDuration = Rand.Range(0.0f, 10.0f);
            CalculatePriority();
        }

        protected override bool Check() => false;
        public override bool CanBeCompleted => true;

        public override bool IsLoop { get => true; set => throw new Exception("Trying to set the value for IsLoop from: " + Environment.StackTrace); }

        private float randomTimer;
        private float randomUpdateInterval = 5;
        public float Random { get; private set; }

        public void CalculatePriority(float max = 0)
        {
            //Random = Rand.Range(0.5f, 1.5f);
            //randomTimer = randomUpdateInterval;
            //max = max > 0 ? max : Math.Min(Math.Min(AIObjectiveManager.RunPriority, AIObjectiveManager.OrderPriority) - 1, 100);
            //float initiative = character.GetSkillLevel("initiative");
            //Priority = MathHelper.Lerp(1, max, MathUtils.InverseLerp(100, 0, initiative * Random));
            Priority = 1;
        }

        public override float GetPriority() => Priority;

        public override void Update(float deltaTime)
        {
            //if (objectiveManager.CurrentObjective == this)
            //{
            //    if (randomTimer > 0)
            //    {
            //        randomTimer -= deltaTime;
            //    }
            //    else
            //    {
            //        CalculatePriority();
            //    }
            //}
        }

        protected override void Act(float deltaTime)
        {
            if (PathSteering == null) { return; }

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

            if (currentTargetIsInvalid || currentTarget == null && HumanAIController.VisibleHulls.Any(h => IsForbidden(h)))
            {
                //don't reset to zero, otherwise the character will keep calling FindTargetHulls 
                //almost constantly when there's a small number of potential hulls to move to
                newTargetTimer = Math.Min(newTargetTimer, 0.5f);
                //standStillTimer = 0.0f;
            }
            else if (character.IsClimbing)
            {
                if (currentTarget == null)
                {
                    newTargetTimer = 0;
                }
                else if (Math.Abs(character.AnimController.TargetMovement.Y) > 0.9f)
                {
                    // Don't allow new targets when climbing straight up or down
                    newTargetTimer = Math.Max(newTargetIntervalMin, newTargetTimer);
                }
            }
            else if (character.AnimController.InWater)
            {
                if (currentTarget == null)
                {
                    newTargetTimer = Math.Min(newTargetTimer, 0.5f);
                }
            }
            if (newTargetTimer <= 0.0f)
            {
                if (!searchingNewHull)
                {
                    //find all available hulls first
                    FindTargetHulls();
                    searchingNewHull = true;
                    return;
                }
                else if (targetHulls.Count > 0)
                {
                    //choose a random available hull
                    currentTarget = ToolBox.SelectWeightedRandom(targetHulls, hullWeights, Rand.RandSync.Unsynced);
                    bool isCurrentHullAllowed = !IsForbidden(character.CurrentHull);
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, currentTarget.SimPosition, errorMsgStr: $"AIObjectiveIdle {character.DisplayName}", nodeFilter: node =>
                    {
                        if (node.Waypoint.CurrentHull == null) { return false; }
                        // Check that there is no unsafe or forbidden hulls on the way to the target
                        if (node.Waypoint.CurrentHull != character.CurrentHull && HumanAIController.UnsafeHulls.Contains(node.Waypoint.CurrentHull)) { return false; }
                        if (isCurrentHullAllowed && IsForbidden(node.Waypoint.CurrentHull)) { return false; }
                        return true;
                    });
                    if (path.Unreachable)
                    {
                        //can't go to this room, remove it from the list and try another room next frame
                        int index = targetHulls.IndexOf(currentTarget);
                        targetHulls.RemoveAt(index);
                        hullWeights.RemoveAt(index);
                        PathSteering.Reset();
                        currentTarget = null;
                        return;
                    }
                    searchingNewHull = false;
                }
                else
                {
                    // Couldn't find a target for some reason -> reset
                    newTargetTimer = Math.Max(newTargetIntervalMin, newTargetTimer);
                    searchingNewHull = false;
                }

                if (currentTarget != null)
                {
                    character.AIController.SelectTarget(currentTarget.AiTarget);
                    string errorMsg = null;
#if DEBUG
                    bool isRoomNameFound = currentTarget.DisplayName != null;
                    errorMsg = "(Character " + character.Name + " idling, target " + (isRoomNameFound ? currentTarget.DisplayName : currentTarget.ToString()) + ")";
#endif
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, currentTarget.SimPosition, errorMsgStr: errorMsg, nodeFilter: node => node.Waypoint.CurrentHull != null);
                    PathSteering.SetPath(path);
                }

                newTargetTimer = currentTarget != null && character.AnimController.InWater ? newTargetIntervalMin : Rand.Range(newTargetIntervalMin, newTargetIntervalMax);
            }
            
            newTargetTimer -= deltaTime;

            //wander randomly 
            // - if reached the end of the path 
            // - if the target is unreachable
            // - if the path requires going outside
            if (!character.IsClimbing)
            {
                if (SteeringManager != PathSteering || (PathSteering.CurrentPath != null &&
                    (PathSteering.CurrentPath.Finished || PathSteering.CurrentPath.Unreachable || PathSteering.CurrentPath.HasOutdoorsNodes)))
                {
                    Wander(deltaTime);
                    return;
                }
            }

            if (currentTarget != null)
            {
                if (SteeringManager == PathSteering)
                {
                    PathSteering.SteeringSeek(character.GetRelativeSimPosition(currentTarget), weight: 1, nodeFilter: node => node.Waypoint.CurrentHull != null);
                }
                else
                {
                    character.AIController.SteeringManager.SteeringSeek(character.GetRelativeSimPosition(currentTarget));
                }
            }
            else
            {
                Wander(deltaTime);
            }
        }

        public void Wander(float deltaTime)
        {
            if (character.IsClimbing) { return; }
            if (!character.AnimController.InWater)
            {
                standStillTimer -= deltaTime;
                if (standStillTimer > 0.0f)
                {
                    walkDuration = Rand.Range(walkDurationMin, walkDurationMax);
                    PathSteering.Reset();
                    return;
                }
                if (standStillTimer < -walkDuration)
                {
                    standStillTimer = Rand.Range(standStillMin, standStillMax);
                }
            }
            PathSteering.Wander(deltaTime);
        }

        private void FindTargetHulls()
        {
            targetHulls.Clear();
            hullWeights.Clear();
            foreach (var hull in Hull.hullList)
            {
                if (HumanAIController.UnsafeHulls.Contains(hull)) { continue; }
                if (hull.Submarine == null) { continue; }
                if (character.Submarine == null) { break; }
                if (hull.Submarine.TeamID != character.Submarine.TeamID) { continue; }
                if (hull.Submarine.Info.Type != character.Submarine.Info.Type) { continue; }
                // If the character is inside, only take connected subs into account.
                if (!character.Submarine.IsEntityFoundOnThisSub(hull, true)) { continue; }
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
                    float weight = hull.Volume;
                    // Prefer rooms that are closer. Avoid rooms that are not in the same level.
                    float yDist = Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y);
                    yDist = yDist > 100 ? yDist * 5 : 0;
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + yDist;
                    float distanceFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, 2500, dist));
                    weight *= distanceFactor;
                    hullWeights.Add(weight);
                }
            }
        }

        public static bool IsForbidden(Hull hull)
        {
            if (hull == null) { return true; }
            string hullName = hull.RoomName;
            if (hullName == null) { return false; }
            return hullName.Contains("ballast", StringComparison.OrdinalIgnoreCase) || hullName.Contains("airlock", StringComparison.OrdinalIgnoreCase);
        }
    }
}
