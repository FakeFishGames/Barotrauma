using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionSkill : AbilityConditionData
    {
        private readonly string skillIdentifier;

        public AbilityConditionSkill(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            skillIdentifier = conditionElement.GetAttributeString("skillidentifier", "").ToLowerInvariant();
        }

        private bool MatchesConditionSpecific(string skillIdentifier)
        {
            return this.skillIdentifier == skillIdentifier;
        }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if ((abilityData as string ?? (abilityData as IAbilityString)?.String) is string skillIdentifier)
            {
                return MatchesConditionSpecific(skillIdentifier);
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(string));
                return false;
            }
        }
    }
}
