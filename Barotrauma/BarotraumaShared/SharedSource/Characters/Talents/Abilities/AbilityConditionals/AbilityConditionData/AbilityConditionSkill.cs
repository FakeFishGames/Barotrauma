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

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilitySkillIdentifier)?.SkillIdentifier is string skillIdentifier)
            {
                return MatchesConditionSpecific(skillIdentifier);
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilitySkillIdentifier));
                return false;
            }
        }
    }
}
