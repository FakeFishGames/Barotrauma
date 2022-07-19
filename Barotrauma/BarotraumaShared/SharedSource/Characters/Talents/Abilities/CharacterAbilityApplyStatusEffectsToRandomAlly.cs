using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToRandomAlly : CharacterAbilityApplyStatusEffects
    {
        private readonly float squaredMaxDistance;
        private readonly bool allowDifferentSub;
        private readonly bool allowSelf;

        public override bool AllowClientSimulation => false;

        public CharacterAbilityApplyStatusEffectsToRandomAlly(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            squaredMaxDistance = MathF.Pow(abilityElement.GetAttributeFloat("maxdistance", float.MaxValue), 2);
            allowDifferentSub = abilityElement.GetAttributeBool("mustbeonsamesub", true);
            allowSelf = abilityElement.GetAttributeBool("allowself", true);
        }

        protected override void ApplyEffect()
        {
            ApplyEffect(Character);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character targetCharacter)
            {
                ApplyEffect(targetCharacter);
            }
            else
            {
                ApplyEffect(Character);
            }
        }

        private void ApplyEffect(Character thisCharacter)
        {
            Character chosenCharacter =
                Character.GetFriendlyCrew(thisCharacter).Where(c =>
                    (allowSelf || c != thisCharacter) &&
                    (allowDifferentSub || c.Submarine == Character.Submarine) &&
                    Vector2.DistanceSquared(thisCharacter.WorldPosition, c.WorldPosition) is float tempDistance &&
                    tempDistance < squaredMaxDistance).GetRandomUnsynced();
            if (chosenCharacter == null) { return; }

            ApplyEffectSpecific(chosenCharacter);

        }
    }
}
