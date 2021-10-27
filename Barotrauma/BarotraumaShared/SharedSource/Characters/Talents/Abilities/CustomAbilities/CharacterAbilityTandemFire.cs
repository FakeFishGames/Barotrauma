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
        private string tag;
        public CharacterAbilityTandemFire(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            tag = abilityElement.GetAttributeString("tag", "");
        }

        protected override void ApplyEffect()
        {
            if (Character.SelectedConstruction == null || !Character.SelectedConstruction.HasTag(tag)) { return; }

            Character closestCharacter = null;
            float closestDistance = float.MaxValue;

            foreach (Character crewCharacter in Character.GetFriendlyCrew(Character))
            {
                if (crewCharacter != Character && Vector2.DistanceSquared(Character.SimPosition, Character.GetRelativeSimPosition(crewCharacter)) is float tempDistance && tempDistance < closestDistance)
                {
                    closestCharacter = crewCharacter;
                    closestDistance = tempDistance;
                }
            }

            if (closestCharacter.SelectedConstruction == null || !closestCharacter.SelectedConstruction.HasTag(tag)) { return; }

            if (closestDistance < squaredMaxDistance)
            {
                ApplyEffectSpecific(Character);
                ApplyEffectSpecific(closestCharacter);
            }
        }
    }
}
