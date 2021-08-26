using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionScavenger : AbilityConditionData
    {
        public AbilityConditionScavenger(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is Item item)
            {
                return item.Submarine != character.Submarine;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(Item));
                return false;
            }
        }
    }
}
