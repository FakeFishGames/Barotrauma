#nullable enable

using System.Collections.Immutable;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityGiveItemStatToTags: CharacterAbility
    {
        private readonly ItemTalentStats stat;
        private readonly float value;
        private readonly ImmutableHashSet<Identifier> tags;
        private readonly bool stackable;

        public CharacterAbilityGiveItemStatToTags(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            stat = abilityElement.GetAttributeEnum("stattype", ItemTalentStats.None);
            value = abilityElement.GetAttributeFloat("value", 0f);
            tags = abilityElement.GetAttributeIdentifierImmutableHashSet("tags", ImmutableHashSet<Identifier>.Empty);
            stackable = abilityElement.GetAttributeBool("stackable", true);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (addingFirstTime)
            {
                VerifyState(conditionsMatched: true, timeSinceLastUpdate: 0.0f);
            }
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
            if (Character?.Submarine is null) { return; }

            foreach (Item item in Character.Submarine.GetItems(true))
            {
                if (item.HasTag(tags) || tags.Contains(item.Prefab.Identifier))
                {
                    item.StatManager.ApplyStat(stat, stackable, value, CharacterTalent);
                }
            }
        }
    }
}