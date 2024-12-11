#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityReduceAffliction : CharacterAbility
    {
        private readonly Identifier afflictionId;
        private readonly float amount;

        public CharacterAbilityReduceAffliction(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            afflictionId = abilityElement.GetAttributeIdentifier("afflictionid", abilityElement.GetAttributeIdentifier("affliction", Identifier.Empty));
            amount = abilityElement.GetAttributeFloat("amount", 0);

            if (afflictionId.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in {nameof(CharacterAbilityReduceAffliction)} - affliction identifier not set.",
                    contentPackage: abilityElement.ContentPackage);
            }
        }

        protected override void ApplyEffect()
        {
            ApplyEffectToCharacter(Character);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityCharacter characterData) 
            { 
                ApplyEffectToCharacter(characterData.Character); 
            }
        }

        private void ApplyEffectToCharacter(Character character)
        {
            character?.CharacterHealth.ReduceAfflictionOnAllLimbs(afflictionId, amount, attacker: Character);
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