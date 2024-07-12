using Barotrauma.Items.Components;

namespace Barotrauma.Abilities
{
    class AbilityConditionItemIsStatic : AbilityConditionData
    {
        public AbilityConditionItemIsStatic(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityItem { Item: var item })
            {
                return item.GetComponent<Holdable>() is null && item.GetComponent<Wearable>() is null;
            }

            return false;
        }
    }
}
