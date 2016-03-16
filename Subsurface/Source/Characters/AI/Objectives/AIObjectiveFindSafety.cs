using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        const float SearchHullInterval = 3.0f;
        const float MinSafety = 50.0f;

        private AIObjectiveGoTo goToObjective;

        private List<Hull> unreachable;

        private float currenthullSafety;

        private float searchHullTimer;

        private AIObjective divingGearObjective;

        public float? OverrideCurrentHullSafety;

        public AIObjectiveFindSafety(Character character)
            : base(character, "")
        {
            unreachable = new List<Hull>();
        }

        protected override void Act(float deltaTime)
        {

            var currentHull = character.AnimController.CurrentHull;

            currenthullSafety = OverrideCurrentHullSafety == null ?
                GetHullSafety(currentHull) : (float)OverrideCurrentHullSafety;

            if (currentHull != null)
            {
                if (currentHull.Volume / currentHull.FullVolume > 0.5f || character.Oxygen < 80.0f)
                {
                    if (!FindDivingGear(deltaTime)) return;
                }

                if (currenthullSafety > MinSafety)
                {
                    character.AIController.SteeringManager.SteeringSeek(currentHull.SimPosition, 0.5f);
                    character.AIController.SelectTarget(null);

                    goToObjective = null;
                    return;
                }
            }

            if (searchHullTimer > 0.0f)
            {
                searchHullTimer -= deltaTime;
            }
            else
            {
                var bestHull = FindBestHull();
                if (bestHull != null)
                {
                    goToObjective = new AIObjectiveGoTo(bestHull, character);
                }

                searchHullTimer = SearchHullInterval;
            }

            if (goToObjective != null)
            {
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;
                if (pathSteering!=null && pathSteering.CurrentPath!= null && 
                    pathSteering.CurrentPath.Unreachable && !unreachable.Contains(goToObjective.Target))
                {
                    unreachable.Add(goToObjective.Target as Hull);
                }


                goToObjective.TryComplete(deltaTime);
            }
        }

        private bool FindDivingGear(float deltaTime)
        {
            if (divingGearObjective==null)
            {
                divingGearObjective = new AIObjectiveFindDivingGear(character, false);
            }

            divingGearObjective.TryComplete(deltaTime);
            return divingGearObjective.IsCompleted();
        }

        private Hull FindBestHull()
        {
            Hull bestHull = null;
            float bestValue = currenthullSafety;

            foreach (Hull hull in Hull.hullList)
            {
                if (hull == character.AnimController.CurrentHull || unreachable.Contains(hull)) continue;

                float hullValue = GetHullSafety(hull);
                hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.X - hull.Position.X));
                hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.Y - hull.Position.Y) * 2.0f);

                if (bestHull == null || hullValue > bestValue)
                {
                    bestHull = hull;
                    bestValue = hullValue;
                }
            }

            return bestHull;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveFindSafety);
        }

        public override float GetPriority(Character character)
        {
            if (character.Oxygen < 80.0f)
            {
                return 150.0f - character.Oxygen;
            }

            if (character.AnimController.CurrentHull == null) return 5.0f;
            currenthullSafety = GetHullSafety(character.AnimController.CurrentHull);
            priority = 100.0f - currenthullSafety;

            if (divingGearObjective != null && !divingGearObjective.IsCompleted()) priority += 20.0f;

            return priority;
        }

        private float GetHullSafety(Hull hull)
        {
            if (hull == null) return 0.0f;

            float waterPercentage = (hull.Volume / hull.FullVolume)*100.0f;
            float fireAmount = 0.0f;

            foreach (FireSource fireSource in hull.FireSources)
            {
                fireAmount += Math.Max(fireSource.Size.X,50.0f);
            }
            
            float safety = 100.0f - fireAmount;
            
            if (waterPercentage > 30.0f && character.OxygenAvailable<=0.0f) safety -= waterPercentage; 
            if (hull.OxygenPercentage < 30.0f) safety -= (30.0f-hull.OxygenPercentage)*5.0f;

            return safety;
        }
    }
}
