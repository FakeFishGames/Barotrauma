using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveMissionCount : CharacterAbility
    {
        private readonly int amount;

        public CharacterAbilityGiveMissionCount(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (!addingFirstTime) { return; }
            if (!(GameMain.GameSession?.Campaign is CampaignMode campaign)) { return; }
            campaign.Settings.AddedMissionCount += amount;
        }
    }
}
