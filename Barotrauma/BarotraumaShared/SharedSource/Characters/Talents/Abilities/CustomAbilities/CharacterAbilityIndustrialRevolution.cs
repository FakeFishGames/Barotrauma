using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityIndustrialRevolution : CharacterAbility
    {
        float addedFabricationSpeed;

        public CharacterAbilityIndustrialRevolution(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedFabricationSpeed = abilityElement.GetAttributeFloat("addedfabricationspeed", 0f);
        }

        public override void UpdateCharacterAbility(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched)
            {
                // not necessarily the cleanest or performant way, but at least this shouldn't break anything.
                // must be done every frame in order to work.
                if (Character.SelectedConstruction?.GetComponent<Fabricator>() is Fabricator fabricator && fabricator.IsActive)
                {
                    fabricator.FabricationSpeedMultiplier += addedFabricationSpeed;
                }
            }
        }
    }
}
