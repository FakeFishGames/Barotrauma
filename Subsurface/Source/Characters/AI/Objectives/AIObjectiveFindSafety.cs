using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        const float SearchHullInterval = 3.0f;
        const float MinSafety = 50.0f;

        AIObjectiveGoTo gotoObjective;

        private List<Hull> unreachable;
        
        float currenthullSafety;

        float searchHullTimer;

        public AIObjectiveFindSafety(Character character)
            : base(character, "")
        {
            unreachable = new List<Hull>();
        }

        protected override void Act(float deltaTime)
        {
            if (character.AnimController.CurrentHull == null || GetHullSafety(character.AnimController.CurrentHull) > MinSafety)
            {
                character.AIController.SteeringManager.SteeringSeek(character.AnimController.CurrentHull.SimPosition);

                character.AIController.SelectTarget(null);

                gotoObjective = null;
                return;
            }

            if (searchHullTimer>0.0f)
            {
                searchHullTimer -= deltaTime;
                return;
            }
            else
            {
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;

                Hull bestHull = null;
                float bestValue = currenthullSafety;

                foreach (Hull hull in Hull.hullList)
                {
                    if (hull == character.AnimController.CurrentHull) continue;
                    if (unreachable.Contains(hull)) continue;

                    float hullValue =  GetHullSafety(hull);
                    hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.X- hull.Position.X));
                    hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.Y - hull.Position.Y)*2.0f);

                    if (bestHull==null || hullValue > bestValue)
                    {
                        bestHull = hull;
                        bestValue = hullValue;
                    }
                }

                if (bestHull != null)
                {
                    var path = pathSteering.PathFinder.FindPath(character.SimPosition, bestHull.SimPosition);
                    if (pathSteering.CurrentPath != null && pathSteering.CurrentPath.Cost < path.Cost && !pathSteering.CurrentPath.Unreachable && gotoObjective!=null)
                    {
                        return;
                    }
                    else
                    {
                        pathSteering.SetPath(path);
                    }

                    gotoObjective = new AIObjectiveGoTo(bestHull, character);
                    //character.AIController.SelectTarget(bestHull.AiTarget);
                }


                searchHullTimer = SearchHullInterval;
            }

            if (gotoObjective != null)
            {
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;
                if (pathSteering!=null && pathSteering.CurrentPath!= null && 
                    pathSteering.CurrentPath.Unreachable && !unreachable.Contains(gotoObjective.Target))
                {
                    unreachable.Add(gotoObjective.Target as Hull);
                }
            }



            gotoObjective.TryComplete(deltaTime);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveFindSafety);
        }

        public override float GetPriority(Character character)
        {
            if (character.AnimController.CurrentHull == null) return 0.0f;
            currenthullSafety = GetHullSafety(character.AnimController.CurrentHull);
            priority = 100.0f - currenthullSafety;
            return priority;
        }

        private float GetHullSafety(Hull hull)
        {
            float waterPercentage = (hull.Volume / hull.FullVolume)*100.0f;
            float fireAmount = 0.0f;

            foreach (FireSource fireSource in hull.FireSources)
            {
                fireAmount += Math.Max(fireSource.Size.X,50.0f);
            }
            
            float safety = 100.0f - fireAmount;
            if (waterPercentage > 30.0f) safety -= waterPercentage; 
            if (hull.OxygenPercentage < 30.0f) safety -= (30.0f-hull.OxygenPercentage)*3.0f;

            return safety;
        }
    }
}
