using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToRandomAlly : CharacterAbilityApplyStatusEffects
    {
        private readonly float squaredMaxDistance;
        private readonly bool allowDifferentSub;
        private readonly bool allowSelf;

        public override bool AllowClientSimulation => false;

        public CharacterAbilityApplyStatusEffectsToRandomAlly(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            squaredMaxDistance = DistanceToSquaredDistance(abilityElement.GetAttributeFloat("maxdistance", float.MaxValue));
            allowDifferentSub = abilityElement.GetAttributeBool("mustbeonsamesub", true);
            allowSelf = abilityElement.GetAttributeBool("allowself", true);
        }

        protected override void ApplyEffect()
        {
            Character chosenCharacter = null;

            chosenCharacter = Character.GetFriendlyCrew(Character).Where(c =>
                    (allowSelf ||c != Character) && 
                    (allowDifferentSub || c.Submarine == Character.Submarine) &&
                    Vector2.DistanceSquared(Character.SimPosition, Character.GetRelativeSimPosition(c)) is float tempDistance &&
                    tempDistance < squaredMaxDistance).GetRandom();

            if (chosenCharacter == null) { return; }

            ApplyEffectSpecific(chosenCharacter);
        }

    }
}
