using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityRegenerateLoot : CharacterAbility
    {
        // not maintained through death, so it's possible for players to respawn and re-loot chests
        // seems like a minor issue for now
        List<Item> openedContainers = new List<Item>();

        public CharacterAbilityRegenerateLoot(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is Item item && !openedContainers.Contains(item))
            {
                openedContainers.Add(item);

                if (item.GetComponent<ItemContainer>() is ItemContainer itemContainer)
                {
                    AutoItemPlacer.RegenerateLoot(item.Submarine, itemContainer);
                }
            }
        }
    }
}
