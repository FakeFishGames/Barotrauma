using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionItemWreck : AbilityConditionData
    {

        public AbilityConditionItemWreck(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityItem)?.Item is Item item)
            {
                return item.Submarine?.Info?.IsWreck ?? false;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItem));
                return false;
            }
        }
    }
}
