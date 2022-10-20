using Microsoft.Xna.Framework;

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
            if (!SelectedItemHasTag(Character, tag)) { return; }

            Character closestCharacter = null;
            float closestDistance = squaredMaxDistance;

            foreach (Character crewCharacter in Character.GetFriendlyCrew(Character))
            {
                if (crewCharacter != Character && 
                    Vector2.DistanceSquared(Character.WorldPosition, crewCharacter.WorldPosition) is float tempDistance && tempDistance < closestDistance &&
                    SelectedItemHasTag(crewCharacter, tag))
                {
                    closestCharacter = crewCharacter;
                    closestDistance = tempDistance;
                }
            }

            if (closestCharacter == null) { return; }

            if (closestDistance < squaredMaxDistance)
            {
                ApplyEffectSpecific(Character);
                ApplyEffectSpecific(closestCharacter);
            }

            static bool SelectedItemHasTag(Character character, string tag) =>
                (character.SelectedItem != null && character.SelectedItem.HasTag(tag)) ||
                (character.SelectedSecondaryItem != null && character.SelectedSecondaryItem.HasTag(tag));
        }
    }
}
