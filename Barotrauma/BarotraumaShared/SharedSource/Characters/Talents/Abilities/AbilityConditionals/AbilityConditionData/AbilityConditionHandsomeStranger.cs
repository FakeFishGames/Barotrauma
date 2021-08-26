using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHandsomeStranger : AbilityConditionData
    {
        string skillIdentifier;

        public AbilityConditionHandsomeStranger(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            skillIdentifier = conditionElement.GetAttributeString("skillidentifier", "").ToLowerInvariant();
        }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is string skillIdentifier)
            {
                return this.skillIdentifier == skillIdentifier;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(string));
                return false;
            }
        }
    }
}
