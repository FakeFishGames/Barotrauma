using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionItemOutsideSubmarine : AbilityConditionData
    {

        public AbilityConditionItemOutsideSubmarine(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityItem)?.Item is Item item)
            {
                return item.Submarine == null || item.Submarine.TeamID != character.Info.TeamID;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItem));
                return false;
            }
        }
    }
}
