using Barotrauma.Extensions;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class MentalStateManager
    {
        private float mentalStateTimer;
        private const float MentalStateInterval = 7.5f;

        private float mentalBehaviorTimer;
        private const float MentalBehaviorInterval = 7.5f;

        private readonly Character character;
        private readonly HumanAIController humanAIController;

        public bool Active { get; set; }
        public MentalType CurrentMentalType { get; private set; }
        public enum MentalType
        {
            Normal,
            Confused, // No effects other than special dialogue
            Afraid, // Will retreat from whoever is nearby
            Desperate, // Will defensively attack/arrest whoever is nearby 
            Berserk // turns fully hostile using team change logic
        }

        private const string MentalTeamChange = "mental";

        public MentalStateManager(Character character, HumanAIController humanAIController)
        {
            this.character = character;
            this.humanAIController = humanAIController;
        }

        public void Update(float deltaTime)
        {
            if (!Active) { return; }
            mentalStateTimer -= deltaTime;
            if (mentalStateTimer <= 0.0f)
            {
                UpdateMentalState();
                mentalStateTimer = MentalStateInterval * Rand.Range(0.75f, 1.25f);
            }

            mentalBehaviorTimer = Math.Max(0f, mentalBehaviorTimer - deltaTime);
        }

        private void UpdateMentalState()
        {
            MentalType newMentalType = GetMentalType(character.CharacterHealth.GetAffliction("psychosis"));
            bool createdCombat = false;

            switch (newMentalType)
            {
                case MentalType.Normal:
                case MentalType.Confused:
                    // remove combat if we became normal again
                    mentalBehaviorTimer = 0f; 
                    break;
                case MentalType.Afraid:
                case MentalType.Desperate:
                case MentalType.Berserk:
                    // berserk is not removed unless we drop to normal behavior again
                    if (CurrentMentalType == MentalType.Berserk)
                    {
                        newMentalType = MentalType.Berserk;
                    }
                    // give players a full interval to react to mental changes
                    if (newMentalType == CurrentMentalType) 
                    {
                        createdCombat = CreateCombatBehavior(CurrentMentalType);
                    }
                    break;
            }

            if (!createdCombat)
            {
                CreateDialogueBehavior(newMentalType);
            }

            if (newMentalType != MentalType.Berserk)
            {
                character.TryRemoveTeamChange(MentalTeamChange);
            }

            CurrentMentalType = newMentalType;
        }

        private int mentalTypeCount;
        private int MentalTypeCount
        {
            get
            {
                if (mentalTypeCount == 0)
                {
                    mentalTypeCount = Enum.GetNames(typeof(MentalType)).Length;
                }
                return mentalTypeCount;
            }
        }

        private MentalType GetMentalType(Affliction affliction)
        {
            if (affliction == null)
            {
                return MentalType.Normal;
            }
            int psychosisIndex = (int)(affliction.Strength / (affliction.Prefab.MaxStrength / MentalTypeCount) * Rand.Range(1f, 1.2f));
            psychosisIndex = Math.Clamp(psychosisIndex, 0, 4);
            MentalType mentalType = psychosisIndex switch
            {
                0 => MentalType.Normal,
                1 => MentalType.Confused,
                2 => MentalType.Afraid,
                3 => MentalType.Desperate,
                4 => MentalType.Berserk,
                _ => throw new ArgumentOutOfRangeException(psychosisIndex.ToString()),
            };
            return mentalType;         
        }

        public bool CreateCombatBehavior(MentalType mentalType)
        {
            Character mentalAttackTarget = Character.CharacterList.Where(
                possibleTarget => HumanAIController.IsActive(possibleTarget) && 
                (possibleTarget.TeamID != character.TeamID || mentalType == MentalType.Berserk) && 
                humanAIController.VisibleHulls.Contains(possibleTarget.CurrentHull) && 
                possibleTarget != character).GetRandomUnsynced();

            if (mentalAttackTarget == null)
            {
                return false;
            }

            var combatMode = AIObjectiveCombat.CombatMode.None;
            bool holdFire = mentalType == MentalType.Afraid && character.IsSecurity;
            switch (mentalType)
            {
                case MentalType.Afraid:
                    combatMode = character.IsSecurity ? AIObjectiveCombat.CombatMode.Arrest : AIObjectiveCombat.CombatMode.Retreat;
                    break;
                case MentalType.Desperate:
                    // might be unnecessary to explicitly declare as arrest against non-humans
                    combatMode = character.IsSecurity && mentalAttackTarget.IsHuman ? AIObjectiveCombat.CombatMode.Arrest : AIObjectiveCombat.CombatMode.Defensive;
                    break;
                case MentalType.Berserk:
                    combatMode = AIObjectiveCombat.CombatMode.Offensive;
                    break;
            }

            // using this as an explicit time-out for the behavior. it's possible it will never run out because of the manager being disabled, but combat objective has failsafes for that
            mentalBehaviorTimer = MentalBehaviorInterval;
            humanAIController.AddCombatObjective(combatMode, mentalAttackTarget, allowHoldFire: holdFire, abortCondition: obj => mentalBehaviorTimer <= 0f);
            Identifier textIdentifier = $"dialogmentalstatereaction{combatMode}".ToIdentifier();
            character.Speak(TextManager.Get(textIdentifier).Value, delay: Rand.Range(0.5f, 1.0f), identifier: textIdentifier, minDurationBetweenSimilar: 25f);

            if (mentalType == MentalType.Berserk && !character.HasTeamChange(MentalTeamChange))
            {
                // TODO: could this be handled in the switch block above?
                character.TryAddNewTeamChange(MentalTeamChange, new ActiveTeamChange(CharacterTeamType.None, ActiveTeamChange.TeamChangePriorities.Absolute, aggressiveBehavior: true));
            }

            return true;
        }

        public void CreateDialogueBehavior(MentalType mentalType)
        {
            if (mentalType == MentalType.Normal) { return; }
            Identifier textIdentifier = $"dialogmentalstate{mentalType}".ToIdentifier();
            character.Speak(TextManager.Get(textIdentifier).Value, delay: Rand.Range(0.5f, 1.0f), identifier: textIdentifier, minDurationBetweenSimilar: 35f);
        }
    }
}
