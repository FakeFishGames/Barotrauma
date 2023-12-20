#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityReplaceAffliction : CharacterAbility
    {
        private readonly Identifier afflictionId;
        private readonly Identifier newAfflictionId;
        private readonly float strengthMultiplier;

        public CharacterAbilityReplaceAffliction(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            afflictionId = abilityElement.GetAttributeIdentifier("afflictionid", abilityElement.GetAttributeIdentifier("affliction", Identifier.Empty));
            newAfflictionId = abilityElement.GetAttributeIdentifier("newafflictionid", abilityElement.GetAttributeIdentifier("newaffliction", Identifier.Empty));

            strengthMultiplier = abilityElement.GetAttributeFloat("strengthmultiplier", 1.0f);

            if (afflictionId.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in {nameof(CharacterAbilityReplaceAffliction)} - affliction identifier not set.");
            }
        }

        protected override void ApplyEffect()
        {
            var affliction = Character.CharacterHealth.GetAffliction(afflictionId);
            if (affliction != null)
            {
                float afflictionStrength = affliction.Strength;
                Limb limb = Character.CharacterHealth.GetAfflictionLimb(affliction);
                Character.CharacterHealth.ReduceAfflictionOnAllLimbs(affliction.Identifier, afflictionStrength);
                if (!newAfflictionId.IsEmpty && AfflictionPrefab.Prefabs.TryGet(newAfflictionId, out var newAfflictionPrefab))
                {
                    Character.CharacterHealth.ApplyAffliction(targetLimb: limb, newAfflictionPrefab.Instantiate(afflictionStrength * strengthMultiplier));
                }
            }
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched)
            {
                ApplyEffect();
            }
        }
    }
}