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
        public override bool ForceRun => true;

        // TODO: expose?
        const float priorityIncrease = 25;
        const float priorityDecrease = 5;
        const float SearchHullInterval = 3.0f;
        public const float HULL_SAFETY_THRESHOLD = 50;

        private List<Hull> unreachable = new List<Hull>();

        private float currenthullSafety;

        private float searchHullTimer;

        private AIObjectiveGoTo goToObjective;
        private AIObjective divingGearObjective;

        public float? OverrideCurrentHullSafety;

        public AIObjectiveFindSafety(Character character) : base(character, "") {  }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        protected override void Act(float deltaTime)
        {
            var currentHull = character.AnimController.CurrentHull;           
            if (HumanAIController.NeedsDivingGear(currentHull))
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
            else if (currenthullSafety < HULL_SAFETY_THRESHOLD)
            {
                var bestHull = FindBestHull();
                if (bestHull != null)
                {
                    if (goToObjective != null)
                    {
                        if (goToObjective.Target != bestHull)
                        {
                            subObjectives.Remove(goToObjective);
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
            Hull bestHull = character.CurrentHull;
            float bestValue = currenthullSafety;
            foreach (Hull hull in Hull.hullList)
            {
                if (unreachable.Contains(hull)) { continue; }
                foreach (var sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(hull, true)) { continue; }
                    float hullValue = GetHullSafety(hull, character);
                    if (character.Submarine == null)
                    {
                        if (hull.RoomName?.ToLowerInvariant() == "airlock")
                        {
                            hullValue = 100;
                        }
                        else
                        {
                            // TODO: could also target gaps that get us inside?
                            foreach (Item item in Item.ItemList)
                            {
                                if (item.CurrentHull == hull && item.HasTag("airlock"))
                                {
                                    hullValue = 100;
                                    break;
                                }
                            }
                        }

                        // Huge preference for closer targets
                        float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 100000, Vector2.Distance(character.WorldPosition, hull.WorldPosition)));
                        hullValue *= distanceFactor;
                    }
                    else
                    {
                        // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
                        float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y) * 2.0f;
                        float distanceFactor = MathHelper.Lerp(1, 0.9f, MathUtils.InverseLerp(0, 10000, dist));
                        hullValue *= distanceFactor;
                        //// Slight preference over hulls that are closer
                        //hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.X - hull.Position.X)) * 0.1f;
                        //hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.Y - hull.Position.Y)) * 0.2f;
                    }
                    if (hullValue > bestValue)
                    {
                        bestHull = hull;
                        bestValue = hullValue;
                    }
                }
            }
            return bestHull;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveFindSafety);
        }

        public override void Update(AIObjectiveManager objectiveManager, float deltaTime)
        {
            if (character.CurrentHull == null)
            {
                currenthullSafety = 0;
                return;
            }
            bool ignoreFire = 
                character.AIController.ObjectiveManager.CurrentObjective is AIObjectiveExtinguishFire || 
                character.AIController.ObjectiveManager.CurrentOrder is AIObjectiveExtinguishFires;
            bool ignoreWater = HumanAIController.HasDivingSuit(character);
            currenthullSafety = OverrideCurrentHullSafety ?? GetHullSafety(character.CurrentHull, character, ignoreWater, ignoreWater, ignoreFire);
            if (currenthullSafety > HULL_SAFETY_THRESHOLD)
            {
                priority -= priorityDecrease * deltaTime;
            }
            else
            {
                float dangerFactor = (100 - currenthullSafety) / 100;
                priority += dangerFactor * priorityIncrease * deltaTime;
            }
            priority = MathHelper.Clamp(priority, 0, 100);
            if (HumanAIController.NeedsDivingGear(character.CurrentHull))
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

        public static float GetHullSafety(Hull hull, Character character, bool ignoreWater = false, bool ignoreOxygen = false, bool ignoreFire = false, bool ignoreEnemies = false)
        {
            if (hull == null) { return 0; }
            if (hull.LethalPressure > 0 && character.PressureProtection <= 0) { return 0; }
            float oxygenFactor = ignoreOxygen ? 1 : MathHelper.Lerp(0, 1, hull.OxygenPercentage / 100);
            float waterFactor =  ignoreWater ? 1 : MathHelper.Lerp(1, 0, hull.WaterPercentage / 100);
            if (!character.NeedsAir)
            {
                oxygenFactor = 1;
                waterFactor = 1;
            }
            // Even the smallest fire reduces the safety by 50%
            float fire = hull.FireSources.Count * 0.5f + hull.FireSources.Sum(fs => fs.DamageRange) / hull.Size.X;
            float fireFactor = ignoreFire ? 1 : MathHelper.Lerp(1, 0, MathHelper.Clamp(fire, 0, 1));
            int enemyCount = Character.CharacterList.Count(e => e.CurrentHull == hull && !e.IsDead && !e.IsUnconscious && (e.AIController is EnemyAIController || e.TeamID != character.TeamID));
            // The hull safety decreases 90% per enemy up to 100% (TODO: test smaller percentages)
            float enemyFactor = ignoreEnemies ? 1 : MathHelper.Lerp(1, 0, MathHelper.Clamp(enemyCount * 0.9f, 0, 1));
            float safety = oxygenFactor * waterFactor * fireFactor * enemyFactor;
            return MathHelper.Clamp(safety * 100, 0, 100);
        }
    }
}
