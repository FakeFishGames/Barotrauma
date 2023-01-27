using System;
using System.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityAlienHoarder : CharacterAbility
    {
        private readonly float addedDamageMultiplierPerItem;
        private readonly float maxAddedDamageMultiplier;
        private readonly string[] tags;

        public CharacterAbilityAlienHoarder(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedDamageMultiplierPerItem = abilityElement.GetAttributeFloat("addeddamagemultiplierperitem", 0f);
            maxAddedDamageMultiplier = abilityElement.GetAttributeFloat("maxaddedddamagemultiplier", float.MaxValue);
            tags = abilityElement.GetAttributeStringArray("tags", Array.Empty<string>(), convertToLowerInvariant: true);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityAttackData attackData)
            {
                float totalAddedDamageMultiplier = 0f;
                foreach (Item item in Character.Inventory.AllItems)
                {
                    if (tags.Any(t => item.Prefab.Tags.Any(p => t == p)))
                    {
                        totalAddedDamageMultiplier += addedDamageMultiplierPerItem;
                    }
                }
                attackData.DamageMultiplier += Math.Min(totalAddedDamageMultiplier, maxAddedDamageMultiplier);
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
