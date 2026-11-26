using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Makes the character pathfind to a random waypoint outside the submarine, then return.
    /// </summary>
    public class AIObjectiveExploreOutside : AIObjective
    {
        public override Identifier Identifier { get; set; } = "explore outside".ToIdentifier();

        protected override bool AllowOutsideSubmarine => true;
        protected override bool AllowInAnySub => true;

        // Note that the bot doesn't unequip diving gear in "wet" rooms, such as the whole outpost entrance module
        public override bool KeepDivingGearOn => state == ExplorationState.GoingOut ||
                                                  state == ExplorationState.ReturningToAirlock;

        private enum ExplorationState
        {
            Starting,
            GoingOut,
            ReturningToAirlock
        }

        private ExplorationState state = ExplorationState.Starting;
        private AIObjectiveGoTo goToObjective;
        private AIObjectiveReturn returnObjective;

        /// <summary>
        /// The submarine (usually outpost) the character was in when exploration started.
        /// </summary>
        private Submarine homeSub;

        private const float MinTargetDistance = 1000.0f;
        private const float MaxTargetDistance = 10000.0f;

        private const int MaxTargetAttempts = 3;
        private int targetAttempts;

        const float CheckSafeToGoOutsideInterval = 10.0f;
        private double lastUnsafeToGoOutsideCheck;
        private bool unsafeToGoOutside;

        /// <summary>
        /// How long the bot may stay outside before turning back, whether it reached the target or not (the target is just for show anyway).
        /// This should be considered in conjunction with the workstation XML defining the minimum amount of oxygen the divers must have before diving.
        /// </summary>
        private const float MaxTimeOutsideBeforeTurnaround = 150.0f;

        /// <summary>
        /// The timestamp when to turn around for a safe(r) dive. Set when the bot first gets outside during this exploration.
        /// </summary>
        private double? turnaroundTime;

        public AIObjectiveExploreOutside(Character character, AIObjectiveManager objectiveManager, float priorityModifier, Identifier option = default) :
            base(character, objectiveManager, priorityModifier, option)
        {
            state = ExplorationState.Starting;
            AbandonIfDisallowed = false;
            AbandonWhenCannotCompleteSubObjectives = false;
        }

        protected override float GetPriority()
        {
            Priority = IsAllowed ?
                (AIObjectiveManager.LowestOrderPriority + AIObjectiveManager.HighestOrderPriority) / 2 :
                0;

            return Priority;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (character.CurrentHull != null) { return; }

            if (state == ExplorationState.GoingOut)
            {
                turnaroundTime ??= Timing.TotalTimeUnpaused + MaxTimeOutsideBeforeTurnaround;
                if (Timing.TotalTimeUnpaused > turnaroundTime.Value)
                {
                    DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] {MaxTimeOutsideBeforeTurnaround}s has passed, time to return");
                    RemoveSubObjective(ref goToObjective);
                    state = ExplorationState.ReturningToAirlock;
                }
            }

            // The dive ending logic is handled here in Update, because (unlike Act) this still runs while a GoTo
            // subobjective is active and while something else (like combat) overrides/drops this objective.
            // We just need to pass the turnaround time to the general AI too so it can enforce it.
            if (turnaroundTime.HasValue && character.AIController is HumanAIController humanAi)
            {
                humanAi.StopInitiatingCombatOutsideAt(turnaroundTime.Value);
            }
        }

        private void CheckUnsafeToGoOutside()
        {
            unsafeToGoOutside = false;
            if (Level.IsLoadedOutpost &&GameMain.GameSession.Missions.Any(static m => m.DisallowExploreOutsideOutpost))
            {
                unsafeToGoOutside = true;
                character.Speak(
                    TextManager.Get("DialogNotSafeToGoOutside").Value, 
                    identifier: "AIObjectiveExploreOutside.DisallowedByMission".ToIdentifier(), 
                    minDurationBetweenSimilar: 120.0f);
            }
        }

        protected override void Act(float deltaTime)
        {
            if (Timing.TotalTimeUnpaused > lastUnsafeToGoOutsideCheck + CheckSafeToGoOutsideInterval)
            {
                CheckUnsafeToGoOutside();
                lastUnsafeToGoOutsideCheck = Timing.TotalTimeUnpaused;
            }
            if (unsafeToGoOutside) { return; }

            switch (state)
            {
                case ExplorationState.Starting:
                    StartExploration();
                    break;
                case ExplorationState.GoingOut:
                    ActGoingOut();
                    break;
                case ExplorationState.ReturningToAirlock:
                    ActReturningToAirlock();
                    break;
            }
        }

        private void StartExploration()
        {
            // If we're outside for some reason at start, just return
            if (character.CurrentHull == null)
            {
                DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Already outside at start, returning");
                state = ExplorationState.ReturningToAirlock;
                return;
            }

            // Too hurt to go out, not worth it just for a day job (workstation will retry later)
            if (character.HealthPercentage < 90f ||
                AIObjectiveRescueAll.GetTreatableAfflictions(character, ignoreTreatmentThreshold: false).Any())
            {
                DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Not healthy enough to go outside, skipping");
                IsCompleted = true;
                return;
            }

            // Save home submarine before leaving
            homeSub ??= character.Submarine;

            // Find a random outside waypoint to explore
            List<WayPoint> potentialTargets = new List<WayPoint>();
            if (Level.Loaded != null)
            {
                foreach (var waypoint in WayPoint.WayPointList)
                {
                    // Only consider level waypoints (not associated with submarines)
                    if (waypoint.Submarine != null) { continue; }
                    if (Hull.FindHull(waypoint.WorldPosition) != null) { continue; }

                    float distSquared = Vector2.DistanceSquared(character.WorldPosition, waypoint.WorldPosition);
                    if (distSquared > MinTargetDistance * MinTargetDistance &&
                        distSquared < MaxTargetDistance * MaxTargetDistance)
                    {
                        potentialTargets.Add(waypoint);
                    }
                }
            }

            if (potentialTargets.Any())
            {
                var target = potentialTargets.GetRandomUnsynced();
                targetAttempts++;
                DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Starting exploration to {target.WorldPosition} (attempt {targetAttempts}/{MaxTargetAttempts})");

                state = ExplorationState.GoingOut;
                TryAddSubObjective(ref goToObjective, () =>
                    new AIObjectiveGoTo(target, character, objectiveManager)
                    {
                        AllowGoingOutside = true,
                        DialogueIdentifier = AIObjectiveGoTo.DialogCannotReachOutside
                    },
                    onAbandon: () =>
                    {
                        DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] GoTo abandoned");
                        RemoveSubObjective(ref goToObjective);
                        if (character.CurrentHull != null && targetAttempts < MaxTargetAttempts)
                        {
                            // Still inside, try another target
                            state = ExplorationState.Starting;
                        }
                        else
                        {
                            state = ExplorationState.ReturningToAirlock;
                        }
                    },
                    onCompleted: () =>
                    {
                        DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Reached target, starting return");
                        RemoveSubObjective(ref goToObjective);
                        state = ExplorationState.ReturningToAirlock;
                    });
            }
            else
            {
                // No valid targets, complete immediately (workstation will retry later)
                DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] No valid waypoints found, completing");
                IsCompleted = true;
            }
        }

        private void ActGoingOut()
        {
            // The goToObjective handles the actual movement, but if we somehow drop it, start the return
            if (goToObjective == null)
            {
                DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Dropped goToObjective, starting return");
                state = ExplorationState.ReturningToAirlock;
            }
        }

        private void ActReturningToAirlock()
        {
            // Clean up any leftover goTo objective
            if (goToObjective != null)
            {
                RemoveSubObjective(ref goToObjective);
            }

            // If we're inside, we're done, the parent objective (currently AIObjectiveOperateItem)
            // will handle navigating to where we need to be
            if (character.CurrentHull != null)
            {
                DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Returned inside, exploration complete");
                RemoveSubObjective(ref returnObjective);
                IsCompleted = true;
                return;
            }

            // Create return objective if we don't have one
            if (returnObjective == null)
            {
                TryAddSubObjective(ref returnObjective, () =>
                    new AIObjectiveReturn(character,
                        orderGiver: null,
                        objectiveManager,
                        preferredTarget: homeSub),
                    onAbandon: () =>
                    {
                        DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Return abandoned, will retry");
                        RemoveSubObjective(ref returnObjective);
                        // Stay in ReturningToAirlock state to retry on next Act()
                    },
                    onCompleted: () =>
                    {
                        DebugConsole.NewMessage($"[ExploreOutside:{character.Name}] Return completed, exploration complete");
                        RemoveSubObjective(ref returnObjective);
                        IsCompleted = true;
                    });
            }
        }

        protected override bool CheckObjectiveState()
        {
            return IsCompleted;
        }

        public override void Reset()
        {
            base.Reset();
            state = ExplorationState.Starting;
            goToObjective = null;
            returnObjective = null;
            homeSub = null;
            targetAttempts = 0;
            turnaroundTime = null;
        }
    }
}
