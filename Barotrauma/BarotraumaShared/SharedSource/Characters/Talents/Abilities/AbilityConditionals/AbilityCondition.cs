using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    abstract class AbilityCondition
    {
        protected CharacterTalent characterTalent;
        protected Character character;
        protected bool invert;

        public virtual bool AllowClientSimulation => true;

        public AbilityCondition(CharacterTalent characterTalent, XElement conditionElement) 
        {
            this.characterTalent = characterTalent;
            character = characterTalent.Character;
            invert = conditionElement.GetAttributeBool("invert", false);
        }
        public abstract bool MatchesCondition(object abilityData);
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
        };

        protected List<TargetType> ParseTargetTypes(string[] targetTypeStrings)
        {
            List<TargetType> targetTypes = new List<TargetType>();
            foreach (string targetTypeString in targetTypeStrings)
            {
                TargetType targetType = TargetType.Any;
                if (!Enum.TryParse(targetTypeString, true, out targetType))
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
                default:
                    return true;
            }
        }

    }
}
