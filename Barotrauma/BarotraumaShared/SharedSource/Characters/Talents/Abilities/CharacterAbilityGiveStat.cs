using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveStat : CharacterAbility
    {
        private StatTypes statType;
        private float value;

        // this and resistance giving should probably be moved directly to charactertalent attributes, as they don't need to interact with either ability group types
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
