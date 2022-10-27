#nullable enable

using System.Collections.Immutable;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityGiveItemStatToTags: CharacterAbility
    {
        private readonly ItemTalentStats stat;
        private readonly float value;
        private readonly ImmutableHashSet<Identifier> tags;

        public CharacterAbilityGiveItemStatToTags(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            stat = abilityElement.GetAttributeEnum("stattype", ItemTalentStats.None);
            value = abilityElement.GetAttributeFloat("value", 0f);
            tags = abilityElement.GetAttributeIdentifierImmutableHashSet("tags", ImmutableHashSet<Identifier>.Empty);
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched)
            {
                ApplyEffect();
            }
        }

        protected override void ApplyEffect()
        {
            foreach (Item item in Character.Submarine.GetItems(true))
            {
                if (item.HasTag(tags) || tags.Contains(item.Prefab.Identifier))
                {
                    item.StatManager.ApplyStat(stat, value, CharacterTalent);
                }
            }
        }
    }
}