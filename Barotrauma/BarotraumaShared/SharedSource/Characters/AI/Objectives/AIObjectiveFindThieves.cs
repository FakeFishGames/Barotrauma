#nullable enable
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindThieves : AIObjectiveLoop<Character>
    {
        public override Identifier Identifier { get; set; } = "find thieves".ToIdentifier();
        protected override float IgnoreListClearInterval => 30;
        public override bool IgnoreUnsafeHulls => true;

        protected override float TargetUpdateTimeMultiplier => 1.0f;

        const float DefaultInspectDistance = 200.0f;

        /// <summary>
        /// How close the NPC must be to the target to the inspect them? You can use high values to make the NPC 
        /// systematically go through targets no matter where they are, and low values to check targets they happen to come across.
        /// </summary>
        public float InspectDistance = DefaultInspectDistance;

        private float? overrideInspectProbability;
        /// <summary>
        /// Chance of inspecting a valid target. The NPC won't try to inspect that target again for <see cref="inspectionInterval"/> 
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
                            campaign.Settings.MaxStolenItemInspectionProbability,
                            campaign.Settings.MinStolenItemInspectionProbability, 
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

        private readonly float inspectionInterval = 120.0f;

        public AIObjectiveFindThieves(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Character target)
        {
            if (!IsValidTarget(target, character)) { return false; }
            if (Vector2.DistanceSquared(target.WorldPosition, character.WorldPosition) > InspectDistance * InspectDistance) { return false; }
            if (lastInspectionTimes.TryGetValue(target, out double lastInspectionTime))
            {
                if (Timing.TotalTime < lastInspectionTime + inspectionInterval)
                {
                    return false;
                }
            }
            return true;
        }

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float TargetEvaluation()
        {
            return subObjectives.Any() ? 50 : 0;
        }

        public void InspectEveryone()
        {
            lastInspectionTimes.Clear();
            overrideInspectProbability = 1.0f;
            InspectDistance = DefaultInspectDistance * 2;
        }

        protected override AIObjective ObjectiveConstructor(Character target)
        {
            var checkStolenItemsObjective = new AIObjectiveCheckStolenItems(character, target, objectiveManager);
            if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced) >= InspectProbability)
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
            if (checkVisibleStolenItemsTimer > 0.0f)
            {
                checkVisibleStolenItemsTimer -= deltaTime;
                return;
            }
            foreach (var target in Character.CharacterList)
            {
                if (!IsValidTarget(target, character)) { continue; }
                //if we spot someone wearing or holding stolen items, immediately check them (with 100% chance of spotting the stolen items)
                if (target.Inventory.AllItems.Any(it => it.SpawnedInCurrentOutpost && !it.AllowStealing && target.HasEquippedItem(it)) &&
                    character.CanSeeTarget(target, seeThroughWindows: true))
                {
                    AIObjectiveCheckStolenItems? existingObjective = 
                        objectiveManager.GetActiveObjectives<AIObjectiveCheckStolenItems>().FirstOrDefault(o => o.TargetCharacter == target);
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
            if (target.IsArrested) { return false; }
            return true;
        }

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
        {
            lastInspectionTimes[target] = Timing.TotalTime;
        }
    }
}
