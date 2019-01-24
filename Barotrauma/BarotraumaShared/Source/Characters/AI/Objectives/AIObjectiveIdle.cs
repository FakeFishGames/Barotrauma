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
        const float WallAvoidDistance = 150.0f;

        private AITarget currentTarget;
        private float newTargetTimer;

        private float standStillTimer;
        private float walkDuration;

        private AIObjectiveFindSafety findSafety;

        public AIObjectiveIdle(Character character) : base(character, "")
        {
            standStillTimer = Rand.Range(-10.0f, 10.0f);
            walkDuration = Rand.Range(0.0f, 10.0f);
        }

        public override bool IsCompleted()
        {
            return false;
        }

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
            if (character.SelectedConstruction != null && character.SelectedConstruction.GetComponent<Ladder>() == null)
            {
                character.SelectedConstruction = null;
            }
            
            if (character.AnimController.InWater)
            {
                //attempt to find a safer place if in water
                if (findSafety == null) findSafety = new AIObjectiveFindSafety(character);
                findSafety.TryComplete(deltaTime);
                return;
            }

            if (newTargetTimer <= 0.0f)
            {
                currentTarget = FindRandomTarget();

                if (currentTarget != null)
                {
                    Vector2 pos = character.SimPosition;
                    if (character != null && character.Submarine == null) { pos -= Submarine.MainSub.SimPosition; }

                    string errorMsg = "(Character " + character.Name + " idling, target "
                        + ((currentTarget.Entity is Hull hull && hull.RoomName != null) ? hull.RoomName : currentTarget.Entity.ToString()) + ")";

                    var path = pathSteering.PathFinder.FindPath(pos, currentTarget.SimPosition, errorMsg);
                    if (path.Cost > 1000.0f && character.AnimController.CurrentHull!=null) return;

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

                //steer away from edges of the hull
                if (character.AnimController.CurrentHull != null)
                {
                    float leftDist = character.Position.X - character.AnimController.CurrentHull.Rect.X;
                    float rightDist = character.AnimController.CurrentHull.Rect.Right - character.Position.X;

                    if (leftDist < WallAvoidDistance && rightDist < WallAvoidDistance)
                    {
                        if (Math.Abs(rightDist - leftDist) > WallAvoidDistance / 2)
                        {
                            pathSteering.SteeringManual(deltaTime, Vector2.UnitX * Math.Sign(rightDist - leftDist));
                        }
                        else
                        {
                            pathSteering.Reset();
                            return;
                        }
                    }
                    else if (leftDist < WallAvoidDistance)
                    {
                        pathSteering.SteeringManual(deltaTime, Vector2.UnitX * (WallAvoidDistance-leftDist)/WallAvoidDistance);
                        pathSteering.WanderAngle = 0.0f;
                        return;
                    }
                    else if (rightDist < WallAvoidDistance)
                    {
                        pathSteering.SteeringManual(deltaTime, -Vector2.UnitX * (WallAvoidDistance-rightDist)/WallAvoidDistance);
                        pathSteering.WanderAngle = MathHelper.Pi;
                        return;
                    }
                }
                
                character.AIController.SteeringManager.SteeringWander();
                //reset vertical steering to prevent dropping down from platforms etc
                character.AIController.SteeringManager.ResetY();                

                return;                
            }
             
            if (currentTarget?.Entity == null) return;
            if (currentTarget.Entity.Removed)
            {
                currentTarget = null;
                return;
            }
            character.AIController.SteeringManager.SteeringSeek(currentTarget.SimPosition, 2.0f);
        }

        private AITarget FindRandomTarget()
        {
            //random chance of navigating back to the room where the character spawned
            if (Rand.Int(5) == 1)
            {
                var idCard = character.Inventory.FindItemByIdentifier("idcard");
                if (idCard == null) return null;

                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (wp.SpawnType != SpawnType.Human || wp.CurrentHull == null) continue;

                    foreach (string tag in wp.IdCardTags)
                    {
                        if (idCard.HasTag(tag)) return wp.CurrentHull.AiTarget;
                    }
                }
            }
            else
            {
                List<Hull> targetHulls = new List<Hull>(Hull.hullList);
                //ignore all hulls with fires or water in them
                targetHulls.RemoveAll(h => h.FireSources.Any() || h.WaterVolume / h.Volume > 0.1f);
                if (character.Submarine != null)
                {
                    targetHulls.RemoveAll(h => h.Submarine != character.Submarine);
                }

                //remove ballast hulls
                foreach (Item item in Item.ItemList)
                {
                    if (item.HasTag("ballast") && targetHulls.Contains(item.CurrentHull))
                    {
                        targetHulls.Remove(item.CurrentHull);
                    }
                }

                //ignore hulls that are too low to stand inside
                if (character.AnimController is HumanoidAnimController animController)
                {
                    float minHeight = ConvertUnits.ToDisplayUnits(animController.HeadPosition.Value);
                    targetHulls.RemoveAll(h => h.CeilingHeight < minHeight);
                }
                if (!targetHulls.Any()) return null;
                
                //prefer larger hulls
                var targetHull = ToolBox.SelectWeightedRandom(targetHulls, targetHulls.Select(h => h.Volume).ToList(), Rand.RandSync.Unsynced);
                return targetHull?.AiTarget;
            }

            return null;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveIdle);
        }
    }
}
