using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveIdle : AIObjective
    {
        const float WallAvoidDistance = 150.0f;

        AITarget currentTarget;
        private float newTargetTimer;



        public AIObjectiveIdle(Character character) : base(character, "")
        {

        }

        public override float GetPriority(Character character)
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
                  
            //wander randomly if reached the end of the path or the target is unreachable
            if (pathSteering==null || (pathSteering.CurrentPath != null && 
                (pathSteering.CurrentPath.NextNode == null || pathSteering.CurrentPath.Unreachable)))
            {
                //steer away from edges of the hull
                if (character.AnimController.CurrentHull!=null)
                {
                    if (character.Position.X < character.AnimController.CurrentHull.Rect.X + WallAvoidDistance)
                    {
                        pathSteering.SteeringManual(deltaTime, Vector2.UnitX*5.0f);
                    }
                    else if (character.Position.X > character.AnimController.CurrentHull.Rect.Right - WallAvoidDistance)
                    {
                        pathSteering.SteeringManual(deltaTime, -Vector2.UnitX);
                    }
                }

                if (character.AnimController.InWater)
                {
                    character.AIController.SteeringManager.SteeringManual(deltaTime, new Vector2(0.0f, 0.5f));
                }
                else
                {
                    character.AIController.SteeringManager.SteeringWander();
                }

                return;                
            }
 
            if (currentTarget == null) return;
            character.AIController.SteeringManager.SteeringSeek(currentTarget.SimPosition);
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
                targetHulls.RemoveAll(h => h.FireSources.Any() || (h.Volume/h.FullVolume)>0.1f);
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
