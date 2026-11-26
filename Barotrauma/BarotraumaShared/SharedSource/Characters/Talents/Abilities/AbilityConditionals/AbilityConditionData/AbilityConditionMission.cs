using Barotrauma.Extensions;
using System.Collections.Immutable;

namespace Barotrauma.Abilities
{
    class AbilityConditionMission : AbilityConditionData
    {
        private readonly ImmutableHashSet<Identifier> missionType;
        private readonly bool isAffiliated;

        public AbilityConditionMission(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            missionType = conditionElement.GetAttributeIdentifierImmutableHashSet("missiontype", ImmutableHashSet<Identifier>.Empty)!;
            isAffiliated = conditionElement.GetAttributeBool("isaffiliated", false);

            if (!missionType.IsEmpty && MissionPrefab.Prefabs.None(m => missionType.Contains(m.Type)))
            {
                DebugConsole.AddWarning($"Potential error in talent \"{characterTalent.DebugIdentifier}\": no mission has the mission type \"{missionType}\".");
            }
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityMission { Mission: { } mission })
            {
                if (!isAffiliated) { return CheckMissionType(); }

                if (GameMain.GameSession?.Campaign is not CampaignMode campaign) { return false; }

                foreach (var reputationReward in mission.ReputationRewards)
                {
                    if (reputationReward.Amount <= 0) { continue; }
                    if (GetMatchingFaction(reputationReward.FactionIdentifier) is { } faction &&
                        Faction.GetPlayerAffiliationStatus(faction) is FactionAffiliation.Positive)
                    {
                        return CheckMissionType();
                    }
                }

                return false;

                Faction GetMatchingFaction(Identifier factionIdentifier) =>
                    factionIdentifier == "location"
                        ? mission.OriginLocation?.Faction
                        : campaign.GetFaction(factionIdentifier);

                bool CheckMissionType() => missionType.IsEmpty || missionType.Contains(mission.Prefab.Type);
            }

            LogAbilityConditionError(abilityObject, typeof(IAbilityMission));
            return false;
        }
    }
}
