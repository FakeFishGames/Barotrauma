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

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is Character character)
            {
                if (!IsViableTarget(targetTypes, character)) { return false; }

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(Character));
                return false;
            }
        }
    }
}
