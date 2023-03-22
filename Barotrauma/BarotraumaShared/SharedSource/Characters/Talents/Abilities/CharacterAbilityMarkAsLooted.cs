namespace Barotrauma.Abilities
{
    internal sealed class  CharacterAbilityMarkAsLooted: CharacterAbility
    {
        private readonly Identifier identifier;
        public CharacterAbilityMarkAsLooted(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            identifier = abilityElement.GetAttributeIdentifier("identifier", Identifier.Empty);
            if (identifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, identifier is empty in {nameof(CharacterAbilityMarkAsLooted)}.");
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is not IAbilityCharacter { Character: { } character }) { return; }

            character.MarkedAsLooted.Add(identifier);
        }
    }
}