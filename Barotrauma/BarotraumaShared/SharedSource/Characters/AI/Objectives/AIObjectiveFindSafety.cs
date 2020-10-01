using FarseerPhysics;
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
        public override bool ConcurrentObjectives => true;
        public override bool AllowOutsideSubmarine => true;
        public override bool AllowInAnySub => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;
        public override bool IsLoop { get => true; set => throw new Exception("Trying to set the value for IsLoop from: " + Environment.StackTrace.CleanupStackTrace()); }

        // TODO: expose?
        const float priorityIncrease = 100;
        const float priorityDecrease = 10;
        const float SearchHullInterval = 3.0f;

        private float currenthullSafety;

        private float searchHullTimer;

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveFindDivingGear divingGearObjective;

        public AIObjectiveFindSafety(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override bool Check() => false;
        public override bool CanBeCompleted => true;

        private bool resetPriority;

        public override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                return Priority;
            }
            if (character.CurrentHull == null)
            {
                Priority = objectiveManager.CurrentOrder is AIObjectiveGoTo && HumanAIController.HasDivingSuit(character) ? 0 : 100;
            }
            else
            {
                if (HumanAIController.NeedsDivingGear(character.CurrentHull, out _) && !HumanAIController.HasDivingGear(character))
                {
                    Priority = 100;
                }
                Priority = MathHelper.Clamp(Priority, 0, 100);
                if (divingGearObjective != null && !divingGearObjective.IsCompleted && divingGearObjective.CanBeCompleted)
                {
                    // Boost the priority while seeking the diving gear
                    Priority = Math.Max(Priority, Math.Min(AIObjectiveManager.OrderPriority + 20, 100));
                }
            }
            return Priority;
        }

        public override void Update(float deltaTime)
        {
            if (retryTimer > 0)
            {
                retryTimer -= deltaTime;
                if (retryTimer <= 0)
                {
                    retryCounter = 0;
                }
            }
            if (resetPriority)
            {
                Priority = 0;
                resetPriority = false;
                return;
            }
            if (character.CurrentHull == null)
            {
                currenthullSafety = 0;
            }
            else
            {
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
            }
        }

        private Hull currentSafeHull;
        private Hull previousSafeHull;
        private bool cannotFindSafeHull;
        private bool cannotFindDivingGear;
        private readonly int findDivingGearAttempts = 2;
        private int retryCounter;
        private readonly float retryResetTime = 5;
        private float retryTimer;
        protected override void Act(float deltaTime)
        {
            var currentHull = character.CurrentHull;
            bool dangerousPressure = currentHull == null || currentHull.LethalPressure > 0 && character.PressureProtection <= 0;
            if (!character.LockHands && (!dangerousPressure || cannotFindSafeHull))
            {
                bool needsDivingGear = HumanAIController.NeedsDivingGear(currentHull, out bool needsDivingSuit);
                bool needsEquipment = false;
                if (needsDivingSuit)
                {
                    needsEquipment = !HumanAIController.HasDivingSuit(character, AIObjectiveFindDivingGear.MIN_OXYGEN);
                }
                else if (needsDivingGear)
                {
                    needsEquipment = !HumanAIController.HasDivingGear(character, AIObjectiveFindDivingGear.MIN_OXYGEN);
                }
                if (needsEquipment)
                {
                    if (cannotFindDivingGear && retryCounter < findDivingGearAttempts)
                    {
                        retryTimer = retryResetTime;
                        retryCounter++;
                        needsDivingSuit = !needsDivingSuit;
                        RemoveSubObjective(ref divingGearObjective);
                    }
                    if (divingGearObjective == null)
                    {
                        cannotFindDivingGear = false;
                        RemoveSubObjective(ref goToObjective);
                        TryAddSubObjective(ref divingGearObjective,
                        constructor: () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager),
                        onAbandon: () =>
                        {
                            searchHullTimer = Math.Min(1, searchHullTimer);
                            cannotFindDivingGear = true;
                            // Don't reset the diving gear objective, because it's possible that there is no diving gear -> seek a safe hull and then reset so that we can check again.
                        },
                        onCompleted: () =>
                        {
                            resetPriority = true;
                            searchHullTimer = Math.Min(1, searchHullTimer);
                            RemoveSubObjective(ref divingGearObjective);
                        });
                    }
                }
            }
            if (divingGearObjective == null || !divingGearObjective.CanBeCompleted)
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
                    currentSafeHull = FindBestHull(allowChangingTheSubmarine: character.TeamID != Character.TeamType.FriendlyNPC);
                    cannotFindSafeHull = currentSafeHull == null || HumanAIController.NeedsDivingGear(currentSafeHull, out _);
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
                            AllowGoingOutside = HumanAIController.HasDivingSuit(character, conditionPercentage: 50)
                        },
                        onCompleted: () =>
                        {
                            if (currenthullSafety > HumanAIController.HULL_SAFETY_THRESHOLD ||
                                HumanAIController.NeedsDivingGear(currentHull, out bool needsSuit) && (needsSuit ? HumanAIController.HasDivingSuit(character) : HumanAIController.HasDivingMask(character)))
                            {
                                resetPriority = true;
                                searchHullTimer = Math.Min(1, searchHullTimer);
                            }
                            RemoveSubObjective(ref goToObjective);
                            if (cannotFindDivingGear)
                            {
                                // If diving gear objective failed, let's reset it here.
                                RemoveSubObjective(ref divingGearObjective);
                            }
                        },
                        onAbandon: () =>
                        {
                            // Don't ignore any hulls if outside, because apparently it happens that we can't find a path, in which case we just want to try again.
                            // If we ignore the hull, it might be the only airlock in the target sub, which ignores the whole sub.
                            if (currentHull != null && goToObjective != null)
                            {
                                if (goToObjective.Target is Hull hull)
                                {
                                    HumanAIController.UnreachableHulls.Add(hull);
                                }
                            }
                            RemoveSubObjective(ref goToObjective);
                        });
                    }
                    else
                    {
                        RemoveSubObjective(ref goToObjective);
                    }
                }
                if (subObjectives.Any(so => so.CanBeCompleted)) { return; }
                if (currentHull != null)
                {
                    //goto objective doesn't exist (a safe hull not found, or a path to a safe hull not found)
                    // -> attempt to manually steer away from hazards
                    Vector2 escapeVel = Vector2.Zero;
                    foreach (Hull hull in HumanAIController.VisibleHulls)
                    {
                        foreach (FireSource fireSource in hull.FireSources)
                        {
                            Vector2 dir = character.Position - fireSource.Position;
                            float distMultiplier = MathHelper.Clamp(100.0f / Vector2.Distance(fireSource.Position, character.Position), 0.1f, 10.0f);
                            escapeVel += new Vector2(Math.Sign(dir.X) * distMultiplier, !character.IsClimbing ? 0 : Math.Sign(dir.Y) * distMultiplier);
                        }
                    }
                    foreach (Character enemy in Character.CharacterList)
                    {
                        if (!HumanAIController.IsActive(enemy) || HumanAIController.IsFriendly(enemy)) { continue; }
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
                objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
            }
        }

        public Hull FindBestHull(IEnumerable<Hull> ignoredHulls = null, bool allowChangingTheSubmarine = true)
        {
            //sort the hulls based on distance and which sub they're in
            //tends to make the method much faster, because we find a potential hull earlier and can discard further-away hulls more easily
            //(for instance, an NPC in an outpost might otherwise go through all the hulls in the main sub first and do tons of expensive
            //path calculations, only to discard all of them when going through the hulls in the outpost)
            float EstimateHullSuitability(Hull hull)
            {
                float dist =
                    Math.Abs(hull.WorldPosition.X - character.WorldPosition.X) +
                    Math.Abs(hull.WorldPosition.Y - character.WorldPosition.Y) * 3;
                float suitability = -dist;
                if (hull.Submarine != character.Submarine)
                {
                    suitability -= 10000.0f;
                }
                return suitability;
            }

            Hull bestHull = null;
            float bestValue = 0;
            foreach (Hull hull in Hull.hullList.OrderByDescending(h => EstimateHullSuitability(h)))
            {
                if (hull.Submarine == null) { continue; }
                if (!allowChangingTheSubmarine && hull.Submarine != character.Submarine) { continue; }
                if (hull.Rect.Height < ConvertUnits.ToDisplayUnits(character.AnimController.ColliderHeightFromFloor) * 2) { continue; }
                if (ignoredHulls != null && ignoredHulls.Contains(hull)) { continue; }
                if (HumanAIController.UnreachableHulls.Contains(hull)) { continue; }
                float hullSafety = 0;
                if (character.CurrentHull != null && character.Submarine != null)
                {
                    // Inside
                    if (!character.Submarine.IsConnectedTo(hull.Submarine)) { continue; }
                    hullSafety = HumanAIController.GetHullSafety(hull, hull.GetConnectedHulls(true, 1), character);
                    float yDist = Math.Abs(character.WorldPosition.Y - hull.WorldPosition.Y);
                    yDist = yDist > 100 ? yDist * 3 : 0;
                    float dist = Math.Abs(character.WorldPosition.X - hull.WorldPosition.X) + yDist;
                    float distanceFactor = MathHelper.Lerp(1, 0.9f, MathUtils.InverseLerp(0, 10000, dist));
                    hullSafety *= distanceFactor;
                    //skip the hull if the safety is already less than the best hull
                    //(no need to do the expensive pathfinding if we already know we're not going to choose this hull)
                    if (hullSafety < bestValue) { continue; }
                    //avoid airlock modules if not allowed to change the sub
                    if (!allowChangingTheSubmarine && hull.OutpostModuleTags.Any(t => t.Equals("airlock", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    // Don't allow to go outside if not already outside.
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, hull.SimPosition, nodeFilter: node => node.Waypoint.CurrentHull != null);
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
                    if (hull.RoomName != null && hull.RoomName.Contains("airlock", StringComparison.OrdinalIgnoreCase))
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
                    // Intentionally exclude wrecks from this check
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

        public override void Reset()
        {
            base.Reset();
            goToObjective = null;
            divingGearObjective = null;
            currentSafeHull = null;
            previousSafeHull = null;
            retryCounter = 0;
            cannotFindDivingGear = false;
            cannotFindSafeHull = false;
        }
    }
}
