using System;
using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    abstract class AbilityCondition
    {
        protected CharacterTalent characterTalent;
        protected Character character;
        protected bool invert;

        public virtual bool AllowClientSimulation => true;

        public AbilityCondition(CharacterTalent characterTalent, ContentXElement conditionElement) 
        {
            this.characterTalent = characterTalent;
            character = characterTalent.Character;
            invert = conditionElement.GetAttributeBool("invert", false);
        }
        public abstract bool MatchesCondition(AbilityObject abilityObject);
        public abstract bool MatchesCondition();


        // tools
        protected enum TargetType
        {
            Any = 0,
            Enemy = 1,
            Ally = 2,
            NotSelf = 3,
            Alive = 4,
            Monster = 5,
            InFriendlySubmarine = 6,
            Large = 7,
        };

        protected List<TargetType> ParseTargetTypes(string[] targetTypeStrings)
        {
            List<TargetType> targetTypes = new List<TargetType>();
            foreach (string targetTypeString in targetTypeStrings)
            {
                if (!Enum.TryParse(targetTypeString, true, out TargetType targetType))
                {
                    DebugConsole.ThrowError("Invalid target type type \"" + targetTypeString + "\" in CharacterTalent (" + characterTalent.DebugIdentifier + ")");
                }
                targetTypes.Add(targetType);
            }
            return targetTypes;
        }

        protected bool IsViableTarget(IEnumerable<TargetType> targetTypes, Character targetCharacter)
        {
            if (targetCharacter == null) { return false; }

            bool isViable = true;
            foreach (TargetType targetType in targetTypes)
            {
                if (!IsViableTarget(targetType, targetCharacter))
                {
                    isViable = false;
                    break;
                }
            }
            return isViable;
        }

        private bool IsViableTarget(TargetType targetType, Character targetCharacter)
        {
            switch (targetType)
            {
                case TargetType.Enemy:
                    return !HumanAIController.IsFriendly(character, targetCharacter);
                case TargetType.Ally:
                    return HumanAIController.IsFriendly(character, targetCharacter);
                case TargetType.NotSelf:
                    return targetCharacter != character;
                case TargetType.Alive:
                    return !targetCharacter.IsDead;
                case TargetType.Monster:
                    return !targetCharacter.IsHuman;
                case TargetType.InFriendlySubmarine:
                    return targetCharacter.Submarine != null && targetCharacter.Submarine.TeamID == character.TeamID;
                case TargetType.Large:
                    // mass of mudraptor is ~48
                    return targetCharacter.AnimController is { Mass: > 50.0f };
                default:
                    return true;
            }
        }

    }
}
