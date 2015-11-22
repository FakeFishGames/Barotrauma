using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        const float SearchHullInterval = 1.0f;
        const float MinSafety = 50.0f;

        AIObjectiveGoTo gotoObjective;
        
        float currenthullSafety;

        float searchHullTimer;

        protected override void Act(float deltaTime, Character character)
        {
            if (character.AnimController.CurrentHull == null || GetHullSafety(character.AnimController.CurrentHull) > MinSafety)
            {
                character.AIController.SteeringManager.SteeringSeek(character.AnimController.CurrentHull.Position);

                gotoObjective = null;
                return;
            }

            if (searchHullTimer>0.0f)
            {
                searchHullTimer -= deltaTime;
                return;
            }

            searchHullTimer = SearchHullInterval;

            Hull bestHull = null;
            float bestValue = currenthullSafety;

            foreach (Hull hull in Hull.hullList)
            {
                if (hull == character.AnimController.CurrentHull) continue;

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
                gotoObjective = new AIObjectiveGoTo(bestHull.AiTarget, character);
                //character.AIController.SelectTarget(bestHull.AiTarget);
            }

            gotoObjective.TryComplete(deltaTime, character);
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
                fireAmount += fireSource.Size.X;
            }
            
            float safety = 100.0f - fireAmount - waterPercentage;
            if (hull.OxygenPercentage < 30.0f) safety -= (30.0f-hull.OxygenPercentage)*3.0f;

            return safety;
        }
    }
}
