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

        private AIObjectiveFindSafety findSafety;

        public AIObjectiveIdle(Character character) : base(character, "")
        {
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

            if (pathSteering==null)
            {
                return;
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
                    if (character != null && character.Submarine == null) pos -= Submarine.MainSub.SimPosition;
                    
                    var path = pathSteering.PathFinder.FindPath(pos, currentTarget.SimPosition);
                    if (path.Cost > 200.0f && character.AnimController.CurrentHull!=null) return;

                    pathSteering.SetPath(path);
                }


                newTargetTimer = currentTarget == null ? 5.0f : 15.0f;
            }
            
            newTargetTimer -= deltaTime;

                  
            //wander randomly 
            // - if reached the end of the path 
            // - if the target is unreachable
            // - if the path requires going outside
            if (pathSteering==null || (pathSteering.CurrentPath != null && 
                (pathSteering.CurrentPath.NextNode == null || pathSteering.CurrentPath.Unreachable || pathSteering.CurrentPath.HasOutdoorsNodes)))
            {
                //steer away from edges of the hull
                if (character.AnimController.CurrentHull!=null)
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
 
            if (currentTarget == null) return;
            character.AIController.SteeringManager.SteeringSeek(currentTarget.SimPosition, 2.0f);
        }

        private AITarget FindRandomTarget()
        {
            if (Rand.Int(5)==1)
            {
                var idCard = character.Inventory.FindItem("ID Card");
                if (idCard==null) return null;

                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (wp.SpawnType != SpawnType.Human || wp.CurrentHull==null) continue;
                 
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
                if (!targetHulls.Any()) return null;

                return targetHulls[Rand.Range(0, targetHulls.Count)].AiTarget;
            }

            return null;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveIdle);
        }
    }
}
