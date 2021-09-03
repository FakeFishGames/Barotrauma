using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityAlienHoarder : CharacterAbility
    {
        private readonly float addedDamageMultiplierPerItem;
        private readonly int maxAmount;
        private readonly string[] tags;

        public CharacterAbilityAlienHoarder(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedDamageMultiplierPerItem = abilityElement.GetAttributeFloat("addeddamagemultiplierperitem", 0f);
            maxAmount = abilityElement.GetAttributeInt("maxamount", 0);
            tags = abilityElement.GetAttributeStringArray("tags", Array.Empty<string>(), convertToLowerInvariant: true);
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is AbilityAttackData attackData)
            {
                float totalAddedDamageMultiplier = 0f;
                foreach (Item item in Character.Inventory.AllItems)
                {
                    if (tags.Any(t => item.Prefab.Tags.Any(p => t == p)))
                    {
                        totalAddedDamageMultiplier += addedDamageMultiplierPerItem;
                    }
                }
                attackData.DamageMultiplier += addedDamageMultiplierPerItem;
            }
            else
            {
                LogAbilityDataMismatch();
            }
        }
    }
}
