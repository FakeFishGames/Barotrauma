#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityGiveItemStat : CharacterAbility
    {
        private readonly ItemTalentStats stat;
        private readonly float value;
        private readonly bool stackable;

        public CharacterAbilityGiveItemStat(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            stat = abilityElement.GetAttributeEnum("stattype", ItemTalentStats.None);
            value = abilityElement.GetAttributeFloat("value", 0f);
            stackable = abilityElement.GetAttributeBool("stackable", true);
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched)
            {
                ApplyEffect();
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is not IAbilityItem ability) { return; }

            ability.Item.StatManager.ApplyStat(stat, stackable, value, CharacterTalent);
        }
    }
}