using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyForce : CharacterAbility
    {
        private readonly float force;
        private readonly float maxVelocity;

        private readonly string afflictionIdentifier;

        private readonly HashSet<LimbType> limbTypes = new HashSet<LimbType>();

        public override bool AppliesEffectOnIntervalUpdate => true;
        public CharacterAbilityApplyForce(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            force = abilityElement.GetAttributeFloat("force", 0f);
            maxVelocity = abilityElement.GetAttributeFloat("maxvelocity", 10f);
            afflictionIdentifier = abilityElement.GetAttributeString("afflictionidentifier", "");

            string[] limbTypesStr = abilityElement.GetAttributeStringArray("limbtypes", new string[0]);
            foreach (string limbTypeStr in limbTypesStr)
            {
                if (Enum.TryParse(limbTypeStr, out LimbType limbType))
                {
                    limbTypes.Add(limbType);
                }
                else
                {
                    DebugConsole.ThrowError($"Error in talent \"{characterAbilityGroup.CharacterTalent.DebugIdentifier}\" - \"{limbTypeStr}\" is not a valid limb type.");
                }
            }
        }

        protected override void ApplyEffect()
        {
            float strength = 1.0f;
            if (!string.IsNullOrEmpty(afflictionIdentifier))
            {
                Affliction affliction = Character.CharacterHealth.GetAffliction(afflictionIdentifier);
                if (affliction == null) { return; }
                strength = affliction.Strength / affliction.Prefab.MaxStrength;
            }

            foreach (Limb limb in Character.AnimController.Limbs)
            {
                if (limb.IsSevered || limb.Removed) { continue; }
                if (limbTypes.Any())
                {
                    if (!limbTypes.Contains(limb.type)) { continue; }
                }
                if (Character.AnimController.TargetMovement.LengthSquared() < 0.001f) { continue; }
                limb.body.ApplyForce(Vector2.Normalize(limb.Mass * Character.AnimController.TargetMovement) * force * strength, maxVelocity);
            }
        }
    }
}
