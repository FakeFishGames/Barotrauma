using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionNoCrewDied : AbilityConditionDataless
    {
        public AbilityConditionNoCrewDied(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
        }

        protected override bool MatchesConditionSpecific()
        {
            if (GameMain.GameSession?.Campaign is CampaignMode campaign)
            {
                return !campaign.CrewHasDied;
            }
            return true;
        }
    }
}
