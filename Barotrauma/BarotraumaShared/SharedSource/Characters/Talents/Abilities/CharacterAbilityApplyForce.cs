using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyForce : CharacterAbility
    {
        private readonly float impulseStrength;
        private readonly float maxVelocity;

        private readonly string afflictionIdentifier;
        public override bool AppliesEffectOnIntervalUpdate => true;
        public CharacterAbilityApplyForce(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            impulseStrength = abilityElement.GetAttributeFloat("impulsestrength", 0f);
            maxVelocity = abilityElement.GetAttributeFloat("maxvelocity", 10f);

            afflictionIdentifier = abilityElement.GetAttributeString("afflictionidentifier", "");
        }

        protected override void ApplyEffect()
        {
            Affliction affliction = Character.CharacterHealth.GetAffliction(afflictionIdentifier);

            if (affliction == null) { return; }

            foreach (Limb limb in Character.AnimController.Limbs)
            {
                limb.body.ApplyForce(Vector2.Normalize(limb.Mass * Character.AnimController.TargetMovement) * impulseStrength * (affliction.Strength / affliction.Prefab.MaxStrength), maxVelocity);
            }
        }
    }
}
