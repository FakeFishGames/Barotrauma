using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityRegenerateLoot : CharacterAbility
    {
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
