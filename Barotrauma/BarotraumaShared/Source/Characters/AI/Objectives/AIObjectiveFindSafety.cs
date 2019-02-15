using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        public override string DebugTag => "find safety";

        const float SearchHullInterval = 3.0f;

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

            currenthullSafety = OverrideCurrentHullSafety == null ? GetHullSafety(currentHull, character) : (float)OverrideCurrentHullSafety;
            
            if (NeedsDivingGear())
            {
                // Stop seeking diving gear if the task is impossible.
                if (divingGearObjective == null || divingGearObjective.CanBeCompleted)
                {
                    if (!FindDivingGear(deltaTime)) return;
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
                    goToObjective = new AIObjectiveGoTo(bestHull, character)
                    {
                        AllowGoingOutside = true
                    };
                }

                searchHullTimer = SearchHullInterval;
            }

            if (goToObjective != null)
            {
                goToObjective.TryComplete(deltaTime);

                if (character.AIController.SteeringManager is IndoorsSteeringManager pathSteering && 
                    pathSteering.CurrentPath != null &&
                    pathSteering.CurrentPath.Unreachable && !unreachable.Contains(goToObjective.Target))
                {
                    unreachable.Add(goToObjective.Target as Hull);
                    goToObjective = null;
                }
            }
            //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
            // -> attempt to manually steer away from hazards
            else if (currentHull != null)
            {
                bool ignoreY = character.SelectedConstruction?.GetComponent<Ladder>() == null;
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, ignoreY ? 0 : Math.Sign(dir.Y) * distMultiplier);                                      
                }

               foreach (Character enemy in Character.CharacterList)
                {
                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious && 
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, ignoreY ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }

                if (escapeVel != Vector2.Zero)
                {
                    escapeVel *= character.AnimController.GetCurrentSpeed(true);
                    //only move if we haven't reached the edge of the room
                    if ((escapeVel.X < 0 && character.Position.X > currentHull.Rect.X + 50) ||
                        (escapeVel.X > 0 && character.Position.X < currentHull.Rect.Right - 50))
                    {
                        character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                    }
                    else
                    {
                        character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                        character.AIController.SteeringManager.Reset();
                    }
                }
                else
                {
                    character.AIController.SteeringManager.Reset();
                }
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
            
            if (character.CurrentHull == null) return 5.0f;
            currenthullSafety = GetHullSafety(character.CurrentHull, character);
            priority = 100.0f - currenthullSafety;

            //var nearbyHulls = character.CurrentHull.GetConnectedHulls(3);

            //increase priority slightly if there's a fire in the room
            //(will increase more heavily if near the damage range of the fire)
            if (character.CurrentHull.FireSources.Count > 0)
            {
                priority += 5.0f;
            }

            /*foreach (Hull hull in nearbyHulls)
            {
                foreach (FireSource fireSource in hull.FireSources)
                {
                    //heavily increase priority if almost within damage range of a fire
                    if (fireSource.IsInDamageRange(character, fireSource.DamageRange * 1.25f))
                    {
                        priority += Math.Max(fireSource.Size.X, 50.0f);
                    }
                }
            }*/
            
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

            bool extinguishFires = 
                character.AIController.ObjectiveManager?.CurrentOrder is AIObjectiveExtinguishFires ||
                character.AIController.ObjectiveManager?.CurrentOrder is AIObjectiveExtinguishFire;

            float fireAmount = 0.0f;
            var nearbyHulls = hull.GetConnectedHulls(3);
            foreach (Hull hull2 in nearbyHulls)
            {
                foreach (FireSource fireSource in hull2.FireSources)
                {
                    //increase priority if near the damage range of a fire
                    //if extinguishing fires, the character can go closer the damage range
                    if (fireSource.IsInDamageRange(character, fireSource.DamageRange * (extinguishFires ? 1.25f : 5.0f)))
                    {
                        fireAmount += Math.Max(fireSource.Size.X, AIObjectiveManager.OrderPriority + 1.0f);
                    }
                }
            }
            safety -= fireAmount;

            foreach (Character enemy in Character.CharacterList)
            {
                if (enemy.CurrentHull == hull && !enemy.IsDead && !enemy.IsUnconscious &&
                   (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                {
                    safety -= 10.0f;
                }
            }
            
            return MathHelper.Clamp(safety, 0.0f, 100.0f);
        }
    }
}
