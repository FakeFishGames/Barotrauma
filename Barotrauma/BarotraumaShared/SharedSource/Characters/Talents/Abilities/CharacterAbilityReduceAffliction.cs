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
                DebugConsole.ThrowError($"Error in {nameof(CharacterAbilityReduceAffliction)} - affliction identifier not set.");
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is not IAbilityCharacter character) { return; }
            character.Character.CharacterHealth.ReduceAfflictionOnAllLimbs(afflictionId, amount);
        }
    }
}