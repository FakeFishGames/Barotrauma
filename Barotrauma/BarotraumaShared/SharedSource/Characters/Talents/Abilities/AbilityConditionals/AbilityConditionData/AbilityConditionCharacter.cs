using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionCharacter : AbilityConditionData
    {
        private readonly List<TargetType> targetTypes;

        public AbilityConditionCharacter(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            targetTypes = ParseTargetTypes(conditionElement.GetAttributeStringArray("targettypes", new string[0], convertToLowerInvariant: true));
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character character)
            {
                if (!IsViableTarget(targetTypes, character)) { return false; }

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityCharacter));
                return false;
            }
        }
    }
}
