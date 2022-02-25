using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionItemInSubmarine : AbilityConditionData
    {
        private readonly SubmarineType? submarineType;

        public AbilityConditionItemInSubmarine(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) 
        { 
            if (conditionElement.Attribute("submarinetype") != null)
            {
                submarineType = conditionElement.GetAttributeEnum<SubmarineType>("submarinetype", SubmarineType.Player);
            }
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityItem)?.Item is Item item)
            {
                if (item.Submarine == null) { return false; }
                if (submarineType.HasValue)
                {
                    return item.Submarine.Info?.Type == submarineType.Value;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItem));
                return false;
            }
        }
    }
}
