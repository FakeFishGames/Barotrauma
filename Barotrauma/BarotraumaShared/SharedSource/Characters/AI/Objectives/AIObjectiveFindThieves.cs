#nullable enable
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class AIObjectiveFindThieves : AIObjectiveLoop<Character>
    {
        public override Identifier Identifier { get; set; } = "find thieves".ToIdentifier();
        protected override float IgnoreListClearInterval => 30;
        public override bool IgnoreUnsafeHulls => true;

        protected override float TargetUpdateTimeMultiplier => 1.0f;

        /// <summary>
        /// How long the round must have ran before NPCs can start doing inspections 
        /// (prevents "unfair" inspections you have no chance to react to if you happen to spawn right next to a security NPC with stolen items on you)
        /// </summary>
        private const float DelayOnRoundStart = 30.0f;
        
        private const float DefaultInspectDistance = 200.0f;
        /// <summary>
        /// Used when something is stolen and when the guards decide to inspect everyone.
        /// </summary>
        private const float ExtendedInspectDistance = 400.0f;
        /// <summary>
        /// Used when the target is tagged as a criminal (= suspective).
        /// </summary>
        private const float CriminalInspectDistance = 500.0f;
        
        private const float CriminalInspectProbability = 1.0f;

        /// <summary>
        /// How close the NPC must be to the target to the inspect them? You can use high values to make the NPC 
        /// systematically go through targets no matter where they are, and low values to check targets they happen to come across.
        /// </summary>
        private float inspectDistance = DefaultInspectDistance;

        private float? overrideInspectProbability;
        /// <summary>
        /// Chance of inspecting a valid target. The NPC won't try to inspect that target again for <see cref="NormalInspectionInterval"/> 
        /// regardless if the target is inspected or not.
        /// </summary>
        public float InspectProbability
        {
            get
            {
                if (overrideInspectProbability.HasValue)
                {
                    return overrideInspectProbability.Value;
                }
                if (GameMain.GameSession?.Campaign is { } campaign)
                {
                    if (campaign.Map?.CurrentLocation?.Reputation is { } reputation)
                    {
                        return MathHelper.Lerp(
                            campaign.Settings.PatdownProbabilityMax,
                            campaign.Settings.PatdownProbabilityMin, 
                            reputation.NormalizedValue);
                    }
                }

                return 0.2f;
            }
        }

        /// <summary>
        /// When did the character last inspect whether some other character has stolen items on them?
        /// </summary>
        private static readonly Dictionary<Character, double> lastInspectionTimes = new Dictionary<Character, double>();
        
        private const float NormalInspectionInterval = 120.0f;
        private const float CriminalInspectionInterval = 30.0f;
        
        public AIObjectiveFindThieves(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool IsValidTarget(Character target)
        {
            if (GameMain.GameSession is not { RoundDuration: > DelayOnRoundStart })
            {
                return false;
            }
            if (!IsValidTarget(target, character)) { return false; }
            float inspectDist = target.IsCriminal ? CriminalInspectDistance : inspectDistance;
            if (Vector2.DistanceSquared(target.WorldPosition, character.WorldPosition) > inspectDist * inspectDist) { return false; }
            if (lastInspectionTimes.TryGetValue(target, out double lastInspectionTime))
            {
                float inspectionInterval = target.IsCriminal ? CriminalInspectionInterval : NormalInspectionInterval;
                if (Timing.TotalTime < lastInspectionTime + inspectionInterval)
                {
                    return false;
                }
            }
            return true;
        }

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float GetTargetPriority()
        {
            if (character.IsClimbing)
            {
                // Don't inspect while climbing, because cannot grab while holding the ladders.
                // Can lead to abandoning the objective when we need to climb the ladders to get to the target, but I think that's acceptable.
                return 0;
            }
            return subObjectives.Any() ? 50 : 0;
        }

        public void InspectEveryone()
        {
            lastInspectionTimes.Clear();
            overrideInspectProbability = 1.0f;
            inspectDistance = ExtendedInspectDistance;
        }

        protected override AIObjective ObjectiveConstructor(Character target)
        {
            var checkStolenItemsObjective = new AIObjectiveCheckStolenItems(character, target, objectiveManager);
            float probabity = target.IsCriminal ? CriminalInspectProbability : InspectProbability;
            if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced) >= probabity)
            {
                checkStolenItemsObjective.ForceComplete();
                lastInspectionTimes[target] = Timing.TotalTime;
            }
            return checkStolenItemsObjective;
        }

        private float checkVisibleStolenItemsTimer;
        private const float CheckVisibleStolenItemsInterval = 5.0f;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (checkVisibleStolenItemsTimer > 0.0f || character.IsClimbing)
            {
                checkVisibleStolenItemsTimer -= deltaTime;
                return;
            }
            if (character.SelectedSecondaryItem?.GetComponent<Controller>() != null)
            {
                // Might be e.g. sitting on a chair.
                character.SelectedSecondaryItem = null;
            }
            foreach (var target in Character.CharacterList)
            {
                if (!IsValidTarget(target, character)) { continue; }
                //if we spot someone wearing or holding stolen items, immediately check them (with 100% chance of spotting the stolen items)
                if (target.Inventory.AllItems.Any(it => it.Illegitimate && target.HasEquippedItem(it)) &&
                    character.CanSeeTarget(target, seeThroughWindows: true))
                {
                    AIObjectiveCheckStolenItems? existingObjective = 
                        objectiveManager.GetActiveObjectives<AIObjectiveCheckStolenItems>().FirstOrDefault(o => o.Target == target);
                    if (existingObjective == null)
                    {
                        objectiveManager.AddObjective(new AIObjectiveCheckStolenItems(character, target, objectiveManager));
                        lastInspectionTimes[target] = Timing.TotalTime;
                    }
                }
            }
            checkVisibleStolenItemsTimer = CheckVisibleStolenItemsInterval;
        }

        private bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.Removed) { return false; }
            if (target.IsIncapacitated) { return false; }
            if (target == character) { return false; }
            if (target.Submarine == null) { return false; }
            if (character.Submarine == null) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (target.Submarine != character.Submarine) { return false; }
            //only player's crew can steal, ignore other teams
            if (!target.IsOnPlayerTeam) { return false; }
            if (target.IsHandcuffed) { return false; }
            // Ignore targets that are climbing, because might need to use ladders to get to them.
            if (target.IsClimbing) { return false; }
            if (HumanAIController.IsTrueForAnyBotInTheCrew(bot => 
                    bot != HumanAIController &&
                    ((bot.ObjectiveManager.GetActiveObjective() is AIObjectiveCheckStolenItems checkObj && checkObj.Target == target) ||
                    (bot.ObjectiveManager.GetActiveObjective() is AIObjectiveCombat combatObj && combatObj.Enemy == target))))
            {
                // Already being inspected by someone or fighting with someone in our team.
                return false;
            }
            return true;
        }

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
        {
            lastInspectionTimes[target] = Timing.TotalTime;
        }

        public override void OnDeselected()
        {
            base.OnDeselected();
            character.DeselectCharacter();
        }
    }
}
