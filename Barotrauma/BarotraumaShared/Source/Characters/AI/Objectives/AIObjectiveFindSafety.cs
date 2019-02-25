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

        // TODO: expose?
        const float priorityIncrease = 20;
        const float priorityDecrease = 5;
        const float SearchHullInterval = 3.0f;
        const float hullSafetyThreshold = 60;

        private List<Hull> hullList;
        private List<Hull> unreachable = new List<Hull>();

        private float currenthullSafety;

        private float searchHullTimer;

        private AIObjectiveGoTo goToObjective;
        private AIObjective divingGearObjective;

        public float? OverrideCurrentHullSafety;

        public AIObjectiveFindSafety(Character character) : base(character, "")
        {
            if (character.Submarine != null)
            {
                hullList = character.Submarine.GetHulls(true);
            }
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted
        {
            get
            {
                return (goToObjective == null || goToObjective.IsCompleted() || goToObjective.CanBeCompleted) &&
                    (divingGearObjective == null || divingGearObjective.IsCompleted() || divingGearObjective.CanBeCompleted);
            }
        }

        protected override void Act(float deltaTime)
        {
            var currentHull = character.AnimController.CurrentHull;

            currenthullSafety = OverrideCurrentHullSafety == null ? GetHullSafety(currentHull, character) : (float)OverrideCurrentHullSafety;
            
            if (HumanAIController.NeedsDivingGear(character))
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
            else if (goToObjective == null || goToObjective.IsCompleted())
            {
                var bestHull = FindBestHull();
                if (bestHull != null)
                {
                    if (goToObjective != null)
                    {
                        if (goToObjective.Target != bestHull)
                        {
                            goToObjective = new AIObjectiveGoTo(bestHull, character)
                            {
                                AllowGoingOutside = true
                            };
                        }
                    }
                    else
                    {
                        goToObjective = new AIObjectiveGoTo(bestHull, character)
                        {
                            AllowGoingOutside = true
                        };
                    }
                }

                searchHullTimer = SearchHullInterval;
            }

            if (goToObjective != null)
            {
                goToObjective.TryComplete(deltaTime);
                if (!goToObjective.CanBeCompleted)
                {
                    if (!unreachable.Contains(goToObjective.Target))
                    {
                        unreachable.Add(goToObjective.Target as Hull);
                    }
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

            foreach (Hull hull in hullList)
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

        public override void UpdatePriority(AIObjectiveManager objectiveManager, float deltaTime)
        {
            if (character.CurrentHull == null) { return; }
            currenthullSafety = GetHullSafety(character.CurrentHull, character);
            if (currenthullSafety > hullSafetyThreshold)
            {
                priority -= priorityDecrease * deltaTime;
            }
            else
            {
                float dangerFactor = (100 - currenthullSafety) / 100;
                priority += dangerFactor * priorityIncrease * deltaTime;
            }
            priority = MathHelper.Clamp(priority, 0, 100);
            if (HumanAIController.NeedsDivingGear(character))
            {
                if (divingGearObjective != null && !divingGearObjective.IsCompleted() && divingGearObjective.CanBeCompleted)
                {
                    priority = Math.Max(priority, AIObjectiveManager.OrderPriority + 10);
                }
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.CurrentHull == null) { return 5; }
            return priority;
        }

        public static float GetHullSafety(Hull hull, Character character)
        {
            if (hull == null) { return 0; }
            if (hull.LethalPressure > 0 && character.PressureProtection <= 0) { return 0; }
            float oxygenFactor = MathHelper.Lerp(0, 1, hull.OxygenPercentage / 100);
            float waterFactor =  MathHelper.Lerp(1, 0, hull.WaterPercentage / 100);
            if (!character.NeedsAir)
            {
                // TODO: use reduced factors if wearing diving gear?
                oxygenFactor = 1;
                waterFactor = 1;
            }
            // Even the smallest fire reduces the safety by 40%
            float fire = hull.FireSources.Count * 0.4f + hull.FireSources.Sum(fs => fs.DamageRange) / hull.Size.X;
            float fireFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(fire, 0, 1));
            if (character.AIController.ObjectiveManager.CurrentObjective is AIObjectiveExtinguishFire ||
                character.AIController.ObjectiveManager.CurrentOrder is AIObjectiveExtinguishFires)
            {
                fireFactor = 1;
            }
            int enemyCount = Character.CharacterList.Count(e => e.CurrentHull == hull && !e.IsDead && !e.IsUnconscious && (e.AIController is EnemyAIController || e.TeamID != character.TeamID));
            // The hull safety decreases 50% per enemy up to 100%
            float enemyFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp((float)enemyCount / 2, 0, 1));
            float safety = oxygenFactor * waterFactor * fireFactor * enemyFactor;
            return MathHelper.Clamp(safety * 100, 0, 100);
        }
    }
}
