#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilitySetMetadataInt : CharacterAbility
    {
        private readonly Identifier identifier;
        private readonly int value;

        public CharacterAbilitySetMetadataInt(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            identifier = abilityElement.GetAttributeIdentifier("identifier", Identifier.Empty);
            value = abilityElement.GetAttributeInt("value", 0);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            ApplyEffect();
        }

        protected override void ApplyEffect()
        {
            if (identifier == Identifier.Empty) { return; }
            if (GameMain.GameSession?.Campaign?.CampaignMetadata is not { } metadata) { return; }

            metadata.SetValue(identifier, value);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }
    }
}