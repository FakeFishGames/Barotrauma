namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveFlag : CharacterAbility
    {
        private readonly AbilityFlags abilityFlag;

        // this and resistance giving should probably be moved directly to charactertalent attributes, as they don't need to interact with either ability group types
        public CharacterAbilityGiveFlag(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            abilityFlag = CharacterAbilityGroup.ParseFlagType(abilityElement.GetAttributeString("flagtype", ""), CharacterTalent.DebugIdentifier);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            Character.AddAbilityFlag(abilityFlag);
        }
    }
}
