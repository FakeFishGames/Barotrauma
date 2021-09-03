using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveStat : CharacterAbility
    {
        private readonly StatTypes statType;
        private readonly float value;

        public CharacterAbilityGiveStat(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            value = abilityElement.GetAttributeFloat("value", 0f);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            Character.ChangeStat(statType, value);
        }
    }
}
