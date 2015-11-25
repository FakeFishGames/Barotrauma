using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveIdle : AIObjective
    {
        AITarget currentTarget;
        private float newTargetTimer;



        public AIObjectiveIdle(Character character) : base(character)
        {

        }

        public override float GetPriority(Character character)
        {
            return 1.0f;
        }


        protected override void Act(float deltaTime)
        {
            if (newTargetTimer <= 0.0f)
            {
                currentTarget = FindRandomTarget();

                newTargetTimer = currentTarget == null ? 5.0f : 10.0f;
            }
            else
            {
                newTargetTimer -= deltaTime;
            }


            if (currentTarget == null) return;
                
            character.AIController.SteeringManager.SteeringSeek(currentTarget.SimPosition);

            var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;
            if (pathSteering!=null && pathSteering.CurrentPath != null)
            {
                if (pathSteering.CurrentPath.NextNode==null || pathSteering.CurrentPath.Unreachable)
                {
                    character.AIController.SteeringManager.SteeringWander(1.0f);
                }
            }
        }

        private AITarget FindRandomTarget()
        {
            if (Rand.Int(5)==1)
            {
                var idCard = character.Inventory.FindItem("ID Card");
                if (idCard==null) return null;

                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (wp.SpawnType != SpawnType.Human) continue;

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
            return true;
        }
    }
}
