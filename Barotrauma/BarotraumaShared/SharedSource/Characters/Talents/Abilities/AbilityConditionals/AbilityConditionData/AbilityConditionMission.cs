using System;
using System.Collections.Immutable;
using System.Linq;

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
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityMission { Mission: { } mission })
            {
                if (!isAffiliated) { return CheckMissionType(); }

                if (GameMain.GameSession?.Campaign?.Factions is not { } factions) { return false; }

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
                        : factions.FirstOrDefault(f => factionIdentifier == f.Prefab.Identifier);

                bool CheckMissionType() => missionType.IsEmpty || missionType.Contains(mission.Prefab.Type);
            }

            LogAbilityConditionError(abilityObject, typeof(IAbilityMission));
            return false;
        }
    }
}
