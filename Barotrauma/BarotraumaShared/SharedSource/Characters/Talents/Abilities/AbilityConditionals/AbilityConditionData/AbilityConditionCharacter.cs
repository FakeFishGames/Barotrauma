using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionCharacter : AbilityConditionData
    {
        private readonly List<TargetType> targetTypes;

        private readonly List<PropertyConditional> conditionals = new List<PropertyConditional>();

        /// <summary>
        /// If enabled, the conditional is checked on the target of the ability (e.g. the character that was killed if the effect type is OnKillCharacter).
        /// Defaults to true, except in the case of <see cref="AbilityConditionHasPermanentStat"/>, which by default targets the character who has the talent.
        /// </summary>
        private readonly bool targetAbilityTarget = false;

        public AbilityConditionCharacter(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            targetTypes = ParseTargetTypes(
                conditionElement.GetAttributeStringArray("targettypes", 
                conditionElement.GetAttributeStringArray("targettype", Array.Empty<string>())));

            foreach (ContentXElement subElement in conditionElement.Elements())
            {
                if (subElement.NameAsIdentifier() == "conditional")
                {
                    conditionals.AddRange(PropertyConditional.FromXElement(subElement));
                }
            }

            //don't log this error if this is a subclass of AbilityConditionCharacter
            //(in that case not having any conditionals here is ok)
            if (!targetTypes.Any() && !conditionals.Any() && GetType() == typeof(AbilityConditionCharacter))
            {
                DebugConsole.ThrowError($"Error in talent \"{characterTalent}\". No target types or conditionals defined - the condition will match any character.",
                    contentPackage: conditionElement.ContentPackage);
            }

            targetAbilityTarget = conditionElement.GetAttributeBool(nameof(targetAbilityTarget), this is not AbilityConditionHasPermanentStat);
        }

        public sealed override bool MatchesCondition()
        {
            //by default data-reliant conditions don't accept null, but in this case it's ok,
            //because we can assume it's the character who has the talent
            return MatchesCondition(abilityObject: null);
        }

        public sealed override bool MatchesCondition(AbilityObject abilityObject)
        {
            return invert ? !MatchesConditionSpecific(abilityObject) : MatchesConditionSpecific(abilityObject);
        }

        protected sealed override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            Character targetCharacter =
                targetAbilityTarget ?
                (abilityObject as IAbilityCharacter)?.Character ?? character :
                character;
            if (targetCharacter is null) { return false; }
            if (!IsViableTarget(targetTypes, targetCharacter)) { return false; }
            foreach (var conditional in conditionals)
            {
                if (!conditional.Matches(targetCharacter)) { return false; }
            }
            return MatchesCharacter(targetCharacter);
        }

        protected virtual bool MatchesCharacter(Character character)
        {
            return true;
        }
    }
}
