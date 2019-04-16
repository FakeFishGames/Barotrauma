using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        public override string DebugTag => "find safety";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        // TODO: expose?
        const float priorityIncrease = 25;
        const float priorityDecrease = 10;
        const float SearchHullInterval = 3.0f;
        const float clearUnreachableInterval = 30;

        public readonly List<Hull> unreachable = new List<Hull>();

        private float currenthullSafety;
        private float unreachableClearTimer;
        private float searchHullTimer;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveFindDivingGear divingGearObjective;

        public AIObjectiveFindSafety(Character character) : base(character, "") {  }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        protected override void Act(float deltaTime)
        {
            var currentHull = character.AnimController.CurrentHull;           
            if (HumanAIController.NeedsDivingGear(currentHull) && divingGearObjective == null)
            {
                bool needsDivingSuit = currentHull == null || currentHull.WaterPercentage > 90;
                bool hasEquipment = needsDivingSuit ? HumanAIController.HasDivingSuit(character) : HumanAIController.HasDivingGear(character);
                if (!hasEquipment)
                {
                    divingGearObjective = new AIObjectiveFindDivingGear(character, needsDivingSuit);
                }
            }
            if (divingGearObjective != null)
            {
                divingGearObjective.TryComplete(deltaTime);
                if (divingGearObjective.IsCompleted())
                {
                    divingGearObjective = null;
                    Priority = 0;
                }
                else if (divingGearObjective.CanBeCompleted)
                {
                    // If diving gear objective is active and can be completed, wait for it to complete.
                    return;
                }
                else
                {
                    divingGearObjective = null;
                    // Reduce the timer so that we get a safe hull target faster.
                    searchHullTimer = Math.Min(1, searchHullTimer);
                }
            }

            if (currenthullSafety < HumanAIController.HULL_SAFETY_THRESHOLD)
            {
                searchHullTimer = Math.Min(1, searchHullTimer);
            }

            if (unreachableClearTimer > 0)
            {
                unreachableClearTimer -= deltaTime;
            }
            else
            {
                unreachableClearTimer = clearUnreachableInterval;
                unreachable.Clear();
            }

            if (searchHullTimer > 0.0f)
            {
                searchHullTimer -= deltaTime;
            }
            else
            {
                var bestHull = FindBestHull();
                if (bestHull != null && bestHull != currentHull)
                {
                    if (goToObjective != null)
                    {
                        if (goToObjective.Target != bestHull)
                        {
                            // If we need diving gear, we should already have it, if possible.
                            goToObjective = new AIObjectiveGoTo(bestHull, character, getDivingGearIfNeeded: false)
                            {
                                AllowGoingOutside = HumanAIController.HasDivingSuit(character)
                            };
                        }
                    }
                    else
                    {
                        goToObjective = new AIObjectiveGoTo(bestHull, character, getDivingGearIfNeeded: false)
                        {
                            AllowGoingOutside = HumanAIController.HasDivingSuit(character)
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
                    HumanAIController.ObjectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
                    //SteeringManager.SteeringWander();
                }
            }
            else if (currentHull != null)
            {
                //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                // -> attempt to manually steer away from hazards
                Vector2 escapeVel = Vector2.Zero;
                foreach (FireSource fireSource in currentHull.FireSources)
                {
                    Vector2 dir = character.Position - fireSource.Position;
                    float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                    escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);                                      
                }

                foreach (Character enemy in Character.CharacterList)
                {
                    //don't run from friendly NPCs
                    if (enemy.TeamID == Character.TeamType.FriendlyNPC) { continue; }
                    //friendly NPCs don't run away from anything but characters controlled by EnemyAIController (= monsters)
                    if (character.TeamID == Character.TeamType.FriendlyNPC && !(enemy.AIController is EnemyAIController)) { continue; }

                    if (enemy.CurrentHull == currentHull && !enemy.IsDead && !enemy.IsUnconscious && 
                        (enemy.AIController is EnemyAIController || enemy.TeamID != character.TeamID))
                    {
                        Vector2 dir = character.Position - enemy.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                }

                if (escapeVel != Vector2.Zero)
                {
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

        public Hull FindBestHull(IEnumerable<Hull> ignoredHulls = null)
        {
            Hull bestHull = null;
            float bestValue = 0;
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine == null) { continue; }
                if (ignoredHulls != null && ignoredHulls.Contains(hull)) { continue; }
                float hullSafety = 0;
                if (character.Submarine != null && SteeringManager == PathSteering)
                {
                    // Inside or outside near the sub
                    if (unreachable.Contains(hull)) { continue; }
                    if (!character.Submarine.IsConnectedTo(hull.Submarine)) { continue; }
                    hullSafety = HumanAIController.GetHullSafety(hull, character);
                    // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y) * 2.0f;
                    float distanceFactor = MathHelper.Lerp(1, 0.9f, MathUtils.InverseLerp(0, 10000, dist));
                    hullSafety *= distanceFactor;
                    //skip the hull if the safety is already less than the best hull
                    //(no need to do the expensive pathfinding if we already know we're not going to choose this hull)
                    if (hullSafety < bestValue) { continue; }
                    // Each unsafe node reduces the hull safety value.
                    // Ignore current hull, because otherwise the would block all paths from the current hull to the target hull.
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, hull.SimPosition);
                    if (path.Unreachable)
                    {
                        unreachable.Add(hull);
                        continue;
                    }
                    int unsafeNodes = path.Nodes.Count(n => n.CurrentHull != character.CurrentHull && HumanAIController.UnsafeHulls.Contains(n.CurrentHull));
                    hullSafety /= 1 + unsafeNodes;
                    // If the target is not inside a friendly submarine, considerably reduce the hull safety.
                    if (!character.Submarine.IsEntityFoundOnThisSub(hull, true))
                    {
                        hullSafety /= 10;
                    }
                }
                else
                {
                    // Outside
                    if (hull.RoomName?.ToLowerInvariant() == "airlock")
                    {
                        hullSafety = 100;
                    }
                    else
                    {
                        // TODO: could also target gaps that get us inside?
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.CurrentHull == hull && item.HasTag("airlock"))
                            {
                                hullSafety = 100;
                                break;
                            }
                        }
                    }
                    // Huge preference for closer targets
                    float distance = Vector2.DistanceSquared(character.WorldPosition, hull.WorldPosition);
                    float distanceFactor = MathHelper.Lerp(1, 0.2f, MathUtils.InverseLerp(0, MathUtils.Pow(100000, 2), distance));
                    hullSafety *= distanceFactor;
                    // If the target is not inside a friendly submarine, considerably reduce the hull safety.
                    if (hull.Submarine.TeamID != character.TeamID && hull.Submarine.TeamID != Character.TeamType.FriendlyNPC)
                    {
                        hullSafety /= 10;
                    }
                }
                if (hullSafety > bestValue)
                {
                    bestHull = hull;
                    bestValue = hullSafety;
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
                Priority = 5;
                return;
            }
            if (character.OxygenAvailable < CharacterHealth.LowOxygenThreshold) { Priority = 100; }
            currenthullSafety = HumanAIController.GetHullSafety(character.CurrentHull);
            if (currenthullSafety > HumanAIController.HULL_SAFETY_THRESHOLD)
            {
                Priority -= priorityDecrease * deltaTime;
            }
            else
            {
                float dangerFactor = (100 - currenthullSafety) / 100;
                Priority += dangerFactor * priorityIncrease * deltaTime;
            }
            Priority = MathHelper.Clamp(Priority, 0, 100);
            if (divingGearObjective != null && !divingGearObjective.IsCompleted() && divingGearObjective.CanBeCompleted)
            {
                Priority = Math.Max(Priority, AIObjectiveManager.OrderPriority + 10);
            }
        }
    }
}
