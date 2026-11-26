using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveReturn : AIObjective
    {
        public override Identifier Identifier { get; set; } = "return".ToIdentifier();
        public Submarine Target { get; private set; }

        private AIObjectiveGoTo moveInsideObjective, moveOutsideObjective;
        private bool usingEscapeBehavior, isSteeringThroughGap;

        /// <summary>
        /// When pathfinding fails outside, swim in a random direction for this duration before retrying.
        /// This helps to "unstuck" when stuck swimming against obstacles, and provides a slightly different
        /// basis for trying again, rather than repeating the same attempt until suffocation.
        /// </summary>
        private float unstuckSwimTimer;
        private Vector2 unstuckSwimDirection;
        private const float UnstuckSwimDuration = 5.0f;

        /// <summary>
        /// Tracks where the last abandon objective happened. If we abandon again in roughly the same position,
        /// we're likely stuck in a loop and should try unstucking.
        /// </summary>
        private Vector2? lastAbandonPosition;
        private const float StuckDistanceThreshold = 200f;

        /// <summary>
        /// When returning from outside, the path is only allowed to go through the target airlock's sub (in addition to
        /// the level itself), so the character doesn't take a detour through some other docked sub's airlock.
        /// If the path keeps failing this many times, we allow other subs too, in case the target airlock is unreachable from outside.
        /// </summary>
        private const int MaxFailedAttemptsBeforeAllowingOtherSubs = 3;
        private int failedMoveOutsideAttempts;

        /// <summary>
        /// If set, other friendly subs' airlocks compete with those of the original target by distance, counted at this multiple of their actual distance.
        /// Otherwise they're only used when the target has no airlock.
        /// </summary>
        private readonly float? otherFriendlySubAirlockDistanceMultiplier;

        /// <summary>
        /// The actual target chosen on creation. <see cref="Target"/> follows the airlock the character heads to, which may be on another sub.
        /// </summary>
        private readonly Submarine originalTarget;

        protected override bool AllowOutsideSubmarine => true;
        protected override bool AllowInAnySub => true;

        public AIObjectiveReturn(Character character, Character orderGiver, AIObjectiveManager objectiveManager, float priorityModifier = 1.0f,
            Submarine preferredTarget = null, float? otherFriendlySubAirlockDistanceMultiplier = null)
            : base(character, objectiveManager, priorityModifier)
        {
            this.otherFriendlySubAirlockDistanceMultiplier = otherFriendlySubAirlockDistanceMultiplier;

            // First priority: use preferred target if specified and still valid
            if (preferredTarget != null)
            {
                if (Submarine.Loaded.Contains(preferredTarget))
                {
                    Target = preferredTarget;
                    DebugConsole.NewMessage($"[Return:{character.Name}] Using preferred target: {Target.Info?.Name}");
                }
                else
                {
                    DebugConsole.NewMessage($"[Return:{character.Name}] Preferred target '{preferredTarget.Info?.Name}' not found in loaded submarines, falling back to search");
                }
            }

            // Fall back to team-based search if no valid preferred target
            if (Target == null)
            {
                // Exhaust home candidates everywhere before settling for any friendly sub, otherwise eg. an outpost
                // dweller would pick the (friendly) player sub just because main subs are searched first.
                // Among merely friendly subs, main subs still win over closer ones like respawn shuttles or drones.
                Target =
                    GetReturnTarget(Submarine.MainSubs, allowAnyFriendlyTeam: false) ??
                    GetReturnTarget(Submarine.Loaded, allowAnyFriendlyTeam: false) ??
                    GetReturnTarget(Submarine.MainSubs, allowAnyFriendlyTeam: true) ??
                    GetReturnTarget(Submarine.Loaded, allowAnyFriendlyTeam: true);
            }

            originalTarget = Target;

            if (Target == null)
            {
                if (GameMain.GameSession?.GameMode is not TestGameMode)
                {
                    DebugConsole.AddWarning($"({character.DisplayName}) No suitable return target found. Cannot return back to the main sub. " +
                        $"PreferredTarget: {preferredTarget?.Info?.Name ?? "none"}, " +
                        $"Loaded subs: {string.Join(", ", Submarine.Loaded.Select(s => $"{s?.Info?.Name ?? "null"}({s?.TeamID})"))}");
                }
                Abandon = true;
            }

            Submarine GetReturnTarget(IEnumerable<Submarine> subs, bool allowAnyFriendlyTeam)
            {
                if (subs.None(sub => sub != null)) { return null; }

                // When following a return order by someone else, return to the order giver's home sub
                Character homeOwner = orderGiver ?? character;
                subs = subs
                        .Where(sub => sub != null)
                        .OrderBy(sub => Vector2.DistanceSquared(character.WorldPosition, sub.WorldPosition));
                var validSubs = subs
                        .Where(homeOwner.IsHomeSubmarine);
                bool isPartOfPlayerCrew = GameSession.GetSessionCrewCharacters(CharacterType.Both).Contains(character);
                Submarine returnTarget = validSubs.FirstOrDefault(sub => isPartOfPlayerCrew == sub.Info.IsPlayer);                

                // If no home sub matched, try any friendly submarine (/outpost)
                if (returnTarget == null && allowAnyFriendlyTeam)
                {
                    returnTarget = subs.FirstOrDefault(s => character.IsOnFriendlyTeam(s.TeamID));
                }
                return returnTarget;
            }
        }

        protected override float GetPriority()
        {
            if (!Abandon && !IsCompleted && objectiveManager.IsOrder(this))
            {
                Priority = objectiveManager.GetOrderPriority(this);
            }
            else
            {
                Priority = AIObjectiveManager.LowestOrderPriority - 1;
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            if (Target == null)
            {
                Abandon = true;
                return;
            }
            bool shouldUseEscapeBehavior = false;
            if (character.CurrentHull != null || isSteeringThroughGap)
            {
                if (character.Submarine == null || !character.Submarine.IsConnectedTo(Target))
                {
                    // Character is on another sub that is not connected to the target sub, use the escape behavior to get them out
                    shouldUseEscapeBehavior = true;
                    if (!usingEscapeBehavior)
                    {
                        HumanAIController.ResetEscape();
                    }
                    isSteeringThroughGap = HumanAIController.Escape(deltaTime);
                    if (!isSteeringThroughGap && (HumanAIController.EscapeTarget == null || HumanAIController.IsCurrentPathUnreachable))
                    {
                        Abandon = true;
                    }
                }
                else if (character.Submarine != Target)
                {
                    // Character is on another sub that is connected to the target sub, create a Go To objective to reach the target sub
                    if (moveInsideObjective == null)
                    {
                        Hull targetHull = null;

                        //find the hull that's linked to a docking port leading to the target sub (i.e. the airlock hull)
                        foreach (var potentialSubmarineDockingPot in Target.ConnectedDockingPorts.Values)
                        {
                            if (!potentialSubmarineDockingPot.Docked) { continue; }
                            if (potentialSubmarineDockingPot.DockingTarget == null) { continue; }
                            //the docking port must be docked to the sub the character is in, or some sub connected to it
                            //(e.g. the extension piece between the sub and the outpost)
                            if (potentialSubmarineDockingPot.DockingTarget.Item.Submarine != character.Submarine) 
                            { 
                                if (!Target.IsConnectedTo(potentialSubmarineDockingPot.DockingTarget.Item.Submarine))
                                {
                                    continue;
                                }
                            }
                            targetHull = potentialSubmarineDockingPot.Item.CurrentHull;
                            break;
                        }
                        if (targetHull != null && !targetHull.IsAirlock)
                        {
                            // Target the closest airlock
                            float closestDist = 0;
                            Hull airlock = null;
                            foreach (Hull hull in Hull.HullList)
                            {
                                if (hull.Submarine != targetHull.Submarine) { continue; }
                                if (!hull.IsAirlock) { continue; }
                                float dist = Vector2.DistanceSquared(targetHull.Position, hull.Position);
                                if (airlock == null || closestDist <= 0 || dist < closestDist)
                                {
                                    airlock = hull;
                                    closestDist = dist;
                                }
                            }
                            if (airlock != null)
                            {
                                targetHull = airlock;
                            }
                        }
                        if (targetHull != null)
                        {
                            RemoveSubObjective(ref moveOutsideObjective);
                            TryAddSubObjective(ref moveInsideObjective,
                                constructor: () => new AIObjectiveGoTo(targetHull, character, objectiveManager)
                                {
                                    AllowGoingOutside = true,
                                    endNodeFilter = n => n.Waypoint.Submarine == targetHull.Submarine
                                },
                                onCompleted: () => RemoveSubObjective(ref moveInsideObjective),
                                onAbandon: () =>
                                {
                                    // Instead of immediately abandoning, clear and retry
                                    RemoveSubObjective(ref moveInsideObjective);
                                });
                        }
                        else
                        {
#if DEBUG
                            DebugConsole.ThrowError("Error with a Return objective: no suitable target for 'moveInsideObjective'");
#endif
                        }
                    }
                }
                else
                {
                    // Character is on the target sub, the objective is completed
                    IsCompleted = true;
                }
            }
            else if (!isSteeringThroughGap && moveOutsideObjective == null)
            {
                // If we're in unstuck swim mode, swim in the given (random) direction
                if (unstuckSwimTimer > 0)
                {
                    unstuckSwimTimer -= deltaTime;
                    SteeringManager.SteeringManual(deltaTime, unstuckSwimDirection);
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: 5, weight: 1);
                    return;
                }

                Hull targetHull = FindAirlockHull();

                if (targetHull != null)
                {
                    // Update Target to match the hull we're going to (for CheckObjectiveState)
                    Target = targetHull.Submarine;

                    RemoveSubObjective(ref moveInsideObjective);
                    bool allowOtherSubsAsFallback = failedMoveOutsideAttempts >= MaxFailedAttemptsBeforeAllowingOtherSubs;
                    TryAddSubObjective(ref moveOutsideObjective,
                        constructor: () => new AIObjectiveGoTo(targetHull, character, objectiveManager)
                        {
                            AllowGoingOutside = true,
                            ExtraNodeFilter = allowOtherSubsAsFallback ?
                                null :
                                // Normally path only through the level and the target's own sub
                                n => n.Waypoint.Submarine == null || n.Waypoint.Submarine == targetHull.Submarine
                        },
                        onCompleted: () => RemoveSubObjective(ref moveOutsideObjective),
                        onAbandon: () =>
                        {
                            RemoveSubObjective(ref moveOutsideObjective);
                            failedMoveOutsideAttempts++;

                            // Check if we're stuck in the same position as last abandon
                            bool isStuckInSameSpot = lastAbandonPosition.HasValue &&
                                Vector2.DistanceSquared(character.WorldPosition, lastAbandonPosition.Value) < StuckDistanceThreshold * StuckDistanceThreshold;

                            if (isStuckInSameSpot)
                            {
                                // We've been stuck in this position for a while now, try to unstuck
                                unstuckSwimTimer = UnstuckSwimDuration;
                                unstuckSwimDirection = Rand.Vector(1f);
                                SteeringManager.Reset();
                                lastAbandonPosition = null;
                            }
                            else
                            {
                                // First abandon from this particular position, so just record it and retry normally
                                lastAbandonPosition = character.WorldPosition;
                            }
                        });
                }
                else
                {
#if DEBUG
                    DebugConsole.ThrowError("Error with a Return objective: no suitable target for 'moveOutsideObjective'");
#endif
                    Abandon = true;
                }
            }
            usingEscapeBehavior = shouldUseEscapeBehavior;
        }

        /// <summary>
        /// Find the nearest airlock hull the character can enter.
        /// Searches the target submarine first, then any other friendly submarines (incl. outposts).
        /// </summary>
        private Hull FindAirlockHull()
        {
            Hull targetHull = null;
            float targetDistanceSquared = float.MaxValue;
            bool targetIsAirlock = false;

            List<Submarine> subsToSearch = new List<Submarine>();
            // Search the target first, then any friendly subs
            if (Target != null)
            {
                subsToSearch.Add(Target);
            }
            subsToSearch.AddRange(Submarine.Loaded.Where(s =>
                s != null && s != Target && character.IsOnFriendlyTeam(s.TeamID)));

            if (subsToSearch.Count == 0)
            {
                return null;
            }

            foreach (var sub in subsToSearch)
            {
                if (sub == null) { continue; }
                float distanceMultiplier = sub != originalTarget && otherFriendlySubAirlockDistanceMultiplier.HasValue ? otherFriendlySubAirlockDistanceMultiplier.Value : 1.0f;
                foreach (var hull in sub.GetHulls(false))
                {
                    bool hullIsAirlock = hull.IsAirlock;
                    if (hullIsAirlock || (!targetIsAirlock && hull.LeadsOutside(character)))
                    {
                        float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, hull.WorldPosition) * distanceMultiplier * distanceMultiplier;
                        if (targetHull == null || (hullIsAirlock && !targetIsAirlock) || distanceSquared < targetDistanceSquared)
                        {
                            targetHull = hull;
                            targetDistanceSquared = distanceSquared;
                            targetIsAirlock = hullIsAirlock;
                        }
                    }
                }
                // If we found an airlock in a higher-priority sub, prefer it (unless other subs' airlocks compete by distance)
                if (!otherFriendlySubAirlockDistanceMultiplier.HasValue && targetIsAirlock && targetHull?.Submarine == sub)
                {
                    break;
                }
            }

            return targetHull;
        }

        protected override bool CheckObjectiveState()
        {
            if (Target == null)
            {
                Abandon = true;
                return false;
            }
            if (character.Submarine == Target)
            {
                IsCompleted = true;
            }
            return IsCompleted;
        }

        public override void Reset()
        {
            base.Reset();
            moveInsideObjective = null;
            moveOutsideObjective = null;
            usingEscapeBehavior = false;
            isSteeringThroughGap = false;
            unstuckSwimTimer = 0;
            unstuckSwimDirection = Vector2.Zero;
            lastAbandonPosition = null;
            failedMoveOutsideAttempts = 0;
            HumanAIController.ResetEscape();
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            SteeringManager?.Reset();
            if (character.IsOnPlayerTeam && objectiveManager.CurrentOrder == objectiveManager.CurrentObjective)
            {
                string msg = TextManager.Get("dialogcannotreturn").Value;
                if (!msg.IsNullOrEmpty())
                {
                    character.Speak(msg, identifier: "dialogcannotreturn".ToIdentifier(), minDurationBetweenSimilar: 5.0f);
                }
            }
        }
    }
}
