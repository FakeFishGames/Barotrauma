using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionSkill : AbilityConditionData
    {
        private readonly string skillIdentifier;

        public AbilityConditionSkill(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            skillIdentifier = conditionElement.GetAttributeString("skillidentifier", "").ToLowerInvariant();
        }

        private bool MatchesConditionSpecific(Identifier skillIdentifier)
        {
            return this.skillIdentifier == skillIdentifier;
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilitySkillIdentifier { SkillIdentifier: Identifier skillIdentifier })
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
