using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveEscapeHandcuffs : AIObjective
    {
        // Used for prisoner escorts to allow them to escape their binds
        public override string Identifier { get; set; } = "escape handcuffs";
        public override bool AllowAutomaticItemUnequipping => true;
        public override bool AllowOutsideSubmarine => true;
        public override bool AllowInAnySub => true;

        private int escapeProgress;
        private bool isBeingWatched;

        private bool shouldSwitchTeams;

        const string EscapeTeamChangeIdentifier = "escape";

        public AIObjectiveEscapeHandcuffs(Character character, AIObjectiveManager objectiveManager, bool shouldSwitchTeams = true, bool beginInstantly = false, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            this.shouldSwitchTeams = shouldSwitchTeams;
            if (beginInstantly)
            {
                escapeTimer = EscapeIntervalTimer;
            }
        }

        public override bool CanBeCompleted => true;
        public override bool IsLoop { get => true; set => throw new Exception("Trying to set the value for IsLoop from: " + Environment.StackTrace.CleanupStackTrace()); }
        protected override bool CheckObjectiveSpecific() => false;

        // escape timer is set to 60 by default to allow players to locate prisoners in time
        private float escapeTimer = 60f;
        private const float EscapeIntervalTimer = 7.5f;

        private float updateTimer;
        private const float UpdateIntervalTimer = 4f;

        protected override float GetPriority()
        {
            Priority = !isBeingWatched && character.LockHands ? AIObjectiveManager.LowestOrderPriority - 1 : 0;
            return Priority;
        }

        public override void Update(float deltaTime)
        {
            updateTimer -= deltaTime;
            if (updateTimer <= 0.0f)
            {
                if (shouldSwitchTeams)
                {
                    if (!character.LockHands)
                    {
                        if (!character.HasTeamChange(EscapeTeamChangeIdentifier))
                        {
                            character.TryAddNewTeamChange(EscapeTeamChangeIdentifier, new ActiveTeamChange(CharacterTeamType.None, ActiveTeamChange.TeamChangePriorities.Willful));
                        }
                    }
                    else
                    {
                        character.TryRemoveTeamChange(EscapeTeamChangeIdentifier);
                    }
                }

                isBeingWatched = false;
                foreach (Character otherCharacter in Character.CharacterList)
                {
                    if (HumanAIController.IsActive(otherCharacter) && otherCharacter.TeamID == CharacterTeamType.Team1 && HumanAIController.VisibleHulls.Contains(otherCharacter.CurrentHull)) // hasn't been tested yet
                    {
                        isBeingWatched = true; // act casual when player characters are around
                        escapeProgress = 0;
                        break;
                    }
                }
                updateTimer = UpdateIntervalTimer * Rand.Range(0.75f, 1.25f);
            }
        }

        protected override void Act(float deltaTime)
        {
            SteeringManager.Reset();

            escapeTimer -= deltaTime;
            if (escapeTimer <= 0.0f)
            {
                escapeProgress += Rand.Range(2, 5);
                if (escapeProgress > 15)
                {
                    Item handcuffs = character.Inventory.FindItemByTag("handlocker");
                    if (handcuffs != null)
                    {
                        handcuffs.Drop(character);
                    }
                }
                escapeTimer = EscapeIntervalTimer * Rand.Range(0.75f, 1.25f);
            }
        }
        public override void Reset()
        {
            base.Reset();
            escapeProgress = 0;
        }
    }
}
