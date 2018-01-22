using Microsoft.Xna.Framework;
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

        public override bool IsCompleted()
        {
            return false;
        }

        protected override void Act(float deltaTime)
        {
            var currentHull = character.AnimController.CurrentHull;

            currenthullSafety = OverrideCurrentHullSafety == null ?
                GetHullSafety(currentHull, character) : (float)OverrideCurrentHullSafety;
            
            if (NeedsDivingGear())
            {
                if (!FindDivingGear(deltaTime)) return;
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
                if (pathSteering != null && pathSteering.CurrentPath != null &&
                    pathSteering.CurrentPath.Unreachable && !unreachable.Contains(goToObjective.Target))
                {
                    unreachable.Add(goToObjective.Target as Hull);
                }


                goToObjective.TryComplete(deltaTime);
            }
        }

        private bool FindDivingGear(float deltaTime)
        {
            if (divingGearObjective == null)
            {
                divingGearObjective = new AIObjectiveFindDivingGear(character, false);
            }

            if (divingGearObjective.IsCompleted()) return true;

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

                float hullValue = GetHullSafety(hull, character);
                //slight preference over hulls that are closer
                hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.X - hull.Position.X)) * 0.1f;
                hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.Y - hull.Position.Y)) * 0.2f;

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

        private bool NeedsDivingGear()
        {
            var currentHull = character.AnimController.CurrentHull;
            if (currentHull == null) return true;

            //there's lots of water in the room -> get a suit
            if (currentHull.WaterVolume / currentHull.Volume > 0.5f) return true;

            if (currentHull.OxygenPercentage < 30.0f) return true;

            return false;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.Oxygen < 80.0f)
            {
                return 150.0f - character.Oxygen;
            }
            
            if (character.AnimController.CurrentHull == null) return 5.0f;
            currenthullSafety = GetHullSafety(character.AnimController.CurrentHull, character);
            priority = 100.0f - currenthullSafety;

            var nearbyHulls = character.AnimController.CurrentHull.GetConnectedHulls(3);

            foreach (Hull hull in nearbyHulls)
            {
                foreach (FireSource fireSource in hull.FireSources)
                {
                    //increase priority if almost within damage range of a fire
                    if (fireSource.IsInDamageRange(character, fireSource.DamageRange * 1.25f))
                    {
                        priority += Math.Max(fireSource.Size.X, 50.0f);
                    }
                }
            }
            
            if (NeedsDivingGear())
            {
                if (divingGearObjective != null && !divingGearObjective.IsCompleted()) priority += 20.0f;
            }

            return priority;
        }

        public static float GetHullSafety(Hull hull, Character character)
        {
            if (hull == null) return 0.0f;

            float safety = 100.0f;

            float waterPercentage = (hull.WaterVolume / hull.Volume) * 100.0f;
            if (hull.LethalPressure > 0.0f && character.PressureProtection <= 0.0f)
            {
                safety -= 100.0f;
            }
            else if (character.OxygenAvailable <= 0.0f)
            {
                safety -= waterPercentage;
            }
            else
            {
                safety -= waterPercentage * 0.1f;
            }

            if (hull.OxygenPercentage < 30.0f) safety -= (30.0f - hull.OxygenPercentage) * 5.0f;

            if (safety <= 0.0f) return 0.0f;
            
            float fireAmount = 0.0f;
            var nearbyHulls = hull.GetConnectedHulls(3);
            foreach (Hull hull2 in nearbyHulls)
            {
                foreach (FireSource fireSource in hull2.FireSources)
                {
                    //increase priority if almost within damage range of a fire
                    if (fireSource.IsInDamageRange(character, fireSource.DamageRange * 1.25f))
                    {
                        fireAmount += Math.Max(fireSource.Size.X, 50.0f);
                    }
                }
            }
            safety -= fireAmount;

            return MathHelper.Clamp(safety, 0.0f, 100.0f);
        }
    }
}
