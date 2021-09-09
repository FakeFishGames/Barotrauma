using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityRegenerateLoot : CharacterAbility
    {
        // separate random chance used for the ability itself to prevent the player
        // from opening/reopening a container until it spawns loot
        private readonly float randomChance;

        // not maintained through death, so it's possible for players to respawn and re-loot chests
        // seems like a minor issue for now
        private readonly List<Item> openedContainers = new List<Item>();

        public CharacterAbilityRegenerateLoot(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            randomChance = abilityElement.GetAttributeFloat("randomchance", 1f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityItem)?.Item is Item item)
            {
                if (openedContainers.Contains(item)) { return; }
                openedContainers.Add(item);
                if (randomChance < Rand.Range(0f, 1f, Rand.RandSync.Unsynced)) { return; }

                if (item.GetComponent<ItemContainer>() is ItemContainer itemContainer)
                {
                    AutoItemPlacer.RegenerateLoot(item.Submarine, itemContainer);
                }
            }
        }
    }
}
