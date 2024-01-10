using Barotrauma.Items.Components;
using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    class CharacterAbilityRegenerateLoot : CharacterAbility
    {
        /// <summary>
        /// Chance for the loot to be regenerated. We can't use <see cref="AbilityConditionServerRandom"/> for this, 
        /// because it'd allow the player to reopen the container until the ability is executed successfully
        /// </summary>
        private readonly float randomChance;

        // separate random chance used for the ability itself to prevent the player
        // from opening/reopening a container until it spawns loot

        /// <summary>
        /// Chance for an individual loot item to be generated.
        /// </summary>
        private readonly float randomChancePerItem = 1.0f;

        // not maintained through death, so it's possible for players to respawn and re-loot chests
        // seems like a minor issue for now
        private readonly HashSet<Item> openedContainers = new HashSet<Item>();

        public CharacterAbilityRegenerateLoot(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            randomChance = abilityElement.GetAttributeFloat(nameof(randomChance), 1f);
            randomChancePerItem = abilityElement.GetAttributeFloat(nameof(randomChancePerItem), 1f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityItem)?.Item is not Item item) { return; }            
            if (openedContainers.Contains(item)) { return; }

            openedContainers.Add(item);
            if (randomChance < Rand.Range(0f, 1f, Rand.RandSync.Unsynced)) { return; }

            if (item.GetComponent<ItemContainer>() is ItemContainer itemContainer)
            {
                AutoItemPlacer.RegenerateLoot(item.Submarine, itemContainer, skipItemProbability: 1.0f - randomChancePerItem);
            }
            
        }
    }
}
