using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityTandemFire : CharacterAbilityApplyStatusEffectsToNearestAlly
    {
        // this should just be its own class, misleading to inherit here
        private readonly string tag;
        public CharacterAbilityTandemFire(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            tag = abilityElement.GetAttributeString("tag", "");
        }

        protected override void ApplyEffect()
        {
            if (!SelectedItemHasTag(Character)) { return; }

            Character closestCharacter = null;
            float closestDistance = squaredMaxDistance;

            foreach (Character crewCharacter in Character.GetFriendlyCrew(Character))
            {
                if (crewCharacter != Character && Vector2.DistanceSquared(Character.SimPosition, Character.GetRelativeSimPosition(crewCharacter)) is float tempDistance && tempDistance < closestDistance)
                {
                    closestCharacter = crewCharacter;
                    closestDistance = tempDistance;
                }
            }

            if (!SelectedItemHasTag(closestCharacter)) { return; }

            if (closestDistance < squaredMaxDistance)
            {
                ApplyEffectSpecific(Character);
                ApplyEffectSpecific(closestCharacter);
            }

            bool SelectedItemHasTag(Character character) =>
                (character.SelectedItem != null && character.SelectedItem.HasTag(tag)) ||
                (character.SelectedSecondaryItem != null && character.SelectedSecondaryItem.HasTag(tag));
        }
    }
}
