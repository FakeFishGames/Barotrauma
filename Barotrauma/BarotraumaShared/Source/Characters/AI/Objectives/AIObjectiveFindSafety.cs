using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        public override string DebugTag => "find safety";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;

        // TODO: expose?
        const float priorityIncrease = 100;
        const float priorityDecrease = 10;
        const float SearchHullInterval = 3.0f;

        private float currenthullSafety;

        private float searchHullTimer;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveFindDivingGear divingGearObjective;

        public AIObjectiveFindSafety(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) {  }

        protected override bool Check() => false;
        public override bool CanBeCompleted => true;

        private bool resetPriority;

        public override void Update(float deltaTime)
        {
            if (resetPriority)
            {
                Priority = 0;
                resetPriority = false;
                return;
            }
            if (character.CurrentHull == null)
            {
                currenthullSafety = 0;
                Priority = objectiveManager.CurrentOrder is AIObjectiveGoTo ? 0 : 100;
                return;
            }
            if (HumanAIController.NeedsDivingGear(character, character.CurrentHull, out _) && !HumanAIController.HasDivingGear(character))
            {
                Priority = 100;
            }
            currenthullSafety = HumanAIController.CurrentHullSafety;
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
            if (divingGearObjective != null && !divingGearObjective.IsCompleted && divingGearObjective.CanBeCompleted)
            {
                // Boost the priority while seeking the diving gear
                Priority = Math.Max(Priority, Math.Min(AIObjectiveManager.OrderPriority + 20, 100));
            }
        }

        private Hull currentSafeHull;
        private Hull previousSafeHull;
        protected override void Act(float deltaTime)
        {
            var currentHull = character.CurrentHull;
            bool needsDivingGear = HumanAIController.NeedsDivingGear(character, currentHull, out bool needsDivingSuit);
            bool needsEquipment = false;
            if (needsDivingSuit)
            {
                needsEquipment = !HumanAIController.HasDivingSuit(character);
            }
            else if (needsDivingGear)
            {
                needsEquipment = !HumanAIController.HasDivingMask(character) && !HumanAIController.HasDivingSuit(character);
            }
            if (needsEquipment)
            {
                TryAddSubObjective(ref divingGearObjective, 
                    constructor: () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager),
                    onAbandon: () =>
                    {
                        resetPriority = true;
                        searchHullTimer = Math.Min(1, searchHullTimer);
                        RemoveSubObjective(ref divingGearObjective);
                    },
                    onCompleted: () =>
                    {
                        resetPriority = true;
                        searchHullTimer = Math.Min(1, searchHullTimer);
                        RemoveSubObjective(ref divingGearObjective);
                    });
            }
            else
            {
                if (currenthullSafety < HumanAIController.HULL_SAFETY_THRESHOLD)
                {
                    searchHullTimer = Math.Min(1, searchHullTimer);
                }
                if (searchHullTimer > 0.0f)
                {
                    searchHullTimer -= deltaTime;
                }
                else
                {
                    searchHullTimer = SearchHullInterval * Rand.Range(0.9f, 1.1f);
                    previousSafeHull = currentSafeHull;
                    currentSafeHull = FindBestHull();
                    if (currentSafeHull == null)
                    {
                        currentSafeHull = previousSafeHull;
                    }
                    if (currentSafeHull != null && currentSafeHull != currentHull)
                    {
                        if (goToObjective?.Target != currentSafeHull)
                        {
                            RemoveSubObjective(ref goToObjective);
                        }
                        TryAddSubObjective(ref goToObjective, 
                            constructor: () => new AIObjectiveGoTo(currentSafeHull, character, objectiveManager, getDivingGearIfNeeded: true)
                            {
                                AllowGoingOutside = HumanAIController.HasDivingSuit(character)
                            },
                            onCompleted: () =>
                            {
                                if (currenthullSafety > HumanAIController.HULL_SAFETY_THRESHOLD || 
                                    HumanAIController.NeedsDivingGear(character, currentHull, out bool needsSuit) && (needsSuit ? HumanAIController.HasDivingSuit(character) : HumanAIController.HasDivingMask(character)))
                                {
                                    resetPriority = true;
                                    searchHullTimer = Math.Min(1, searchHullTimer);
                                }
                                RemoveSubObjective(ref goToObjective);
                            },
                            onAbandon: () =>
                            {
                                HumanAIController.UnreachableHulls.Add(goToObjective.Target as Hull);
                                RemoveSubObjective(ref goToObjective);
                            });
                    }
                    else
                    {
                        RemoveSubObjective(ref goToObjective);
                    }
                }
                if (currentHull != null)
                {
                    //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                    // -> attempt to manually steer away from hazards
                    Vector2 escapeVel = Vector2.Zero;
                    // TODO: optimize
                    foreach (FireSource fireSource in HumanAIController.VisibleHulls.SelectMany(h => h.FireSources))
                    {
                        Vector2 dir = character.Position - fireSource.Position;
                        float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                        escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                    }
                    foreach (Character enemy in Character.CharacterList)
                    {
                        if (enemy.IsDead || enemy.IsUnconscious || enemy.Removed || HumanAIController.IsFriendly(enemy)) { continue; }
                        if (HumanAIController.VisibleHulls.Contains(enemy.CurrentHull))
                        {
                            Vector2 dir = character.Position - enemy.Position;
                            float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(enemy.Position, character.Position), 0.1f, 10.0f);
                            escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                        }
                    }
                    if (escapeVel != Vector2.Zero)
                    {
                        float left = currentHull.Rect.X + 50;
                        float right = currentHull.Rect.Right - 50;
                        //only move if we haven't reached the edge of the room
                        if (escapeVel.X < 0 && character.Position.X > left || escapeVel.X > 0 && character.Position.X < right)
                        {
                            character.AIController.SteeringManager.SteeringManual(deltaTime, escapeVel);
                        }
                        else
                        {
                            character.AnimController.TargetDir = escapeVel.X < 0.0f ? Direction.Right : Direction.Left;
                            character.AIController.SteeringManager.Reset();
                        }
                        return;
                    }
                }
                if (divingGearObjective == null && goToObjective == null)
                {
                    objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
                }
            }
        }

        public Hull FindBestHull(IEnumerable<Hull> ignoredHulls = null, bool allowChangingTheSubmarine = true)
        {
            Hull bestHull = null;
            float bestValue = 0;
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine == null) { continue; }
                if (!allowChangingTheSubmarine && hull.Submarine != character.Submarine) { continue; }
                if (ignoredHulls != null && ignoredHulls.Contains(hull)) { continue; }
                if (HumanAIController.UnreachableHulls.Contains(hull)) { continue; }
                float hullSafety = 0;
                if (character.CurrentHull != null && character.Submarine != null)
                {
                    // Inside
                    if (!character.Submarine.IsConnectedTo(hull.Submarine)) { continue; }
                    hullSafety = HumanAIController.GetHullSafety(hull, character);
                    // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y) * 2.0f;
                    float distanceFactor = MathHelper.Lerp(1, 0.9f, MathUtils.InverseLerp(0, 10000, dist));
                    hullSafety *= distanceFactor;
                    //skip the hull if the safety is already less than the best hull
                    //(no need to do the expensive pathfinding if we already know we're not going to choose this hull)
                    if (hullSafety < bestValue) { continue; }
                    // Don't allow to go outside if not already outside.
                    var path = character.CurrentHull != null ? 
                        PathSteering.PathFinder.FindPath(character.SimPosition, hull.SimPosition, nodeFilter: node => node.Waypoint.CurrentHull != null) : 
                        PathSteering.PathFinder.FindPath(character.SimPosition, hull.SimPosition);
                    if (path.Unreachable)
                    {
                        HumanAIController.UnreachableHulls.Add(hull);
                        continue;
                    }
                    // Each unsafe node reduces the hull safety value.
                    // Ignore the current hull, because otherwise we couldn't find a path out.
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
                    if (hull.RoomName != null && hull.RoomName.ToLowerInvariant().Contains("airlock"))
                    {
                        hullSafety = 100;
                    }
                    else
                    {
                        // TODO: could also target gaps that get us inside?
                        foreach (Item item in Item.ItemList)
                        {
                            if (item.CurrentHull != hull && item.HasTag("airlock"))
                            {
                                hullSafety = 100;
                                break;
                            }
                        }
                    }
                    // TODO: could we get a closest door to the outside and target the flowing hull if no airlock is found?
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
    }
}
