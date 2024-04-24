namespace Barotrauma.Abilities
{
    [TypePreviouslyKnownAs("AbilityConditionItemInSubmarine")]
    class AbilityConditionInSubmarine : AbilityConditionData
    {
        private readonly SubmarineType? submarineType;

        public AbilityConditionInSubmarine(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) 
        { 
            if (conditionElement.GetAttribute("submarinetype") != null)
            {
                submarineType = conditionElement.GetAttributeEnum("submarinetype", SubmarineType.Player);
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
                return MatchesCondition();
            }
        }

        public override bool MatchesCondition()
        {
            if (character.Submarine is null) { return false; }

            return character.Submarine?.Info?.Type == submarineType;
        }
    }
}
