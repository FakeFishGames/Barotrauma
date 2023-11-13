#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityGiveReputation : CharacterAbility
    {
        private readonly Identifier factionIdentifier;
        private readonly float amount;

        public CharacterAbilityGiveReputation(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            factionIdentifier = abilityElement.GetAttributeIdentifier("identifier", Identifier.Empty);
            amount = abilityElement.GetAttributeFloat("amount", 0f);
            if (factionIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, faction identifier not defined.");
            }
            if (amount == 0)
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, amount of reputation to give is 0.");
            }
        }

        protected override void ApplyEffect()
        {
            if (GameMain.GameSession?.Campaign is not { } campaign) { return; }

            foreach (Faction faction in campaign.Factions)
            {
                if (faction.Prefab.Identifier != factionIdentifier) { continue; }

                faction.Reputation.AddReputation(amount);
                break;
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject) => ApplyEffect();
    }
}