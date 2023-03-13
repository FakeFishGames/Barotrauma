using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionMission : AbilityConditionData
    {
        private readonly ImmutableHashSet<MissionType> missionType;
        private readonly bool isAffiliated;

        public AbilityConditionMission(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            string[] missionTypeStrings = conditionElement.GetAttributeStringArray("missiontype", new []{ "None" })!;
            HashSet<MissionType> missionTypes = new HashSet<MissionType>();
            isAffiliated = conditionElement.GetAttributeBool("isaffiliated", false);

            foreach (string missionTypeString in missionTypeStrings)
            {
                if (!Enum.TryParse(missionTypeString, out MissionType parsedMission) || parsedMission is MissionType.None)
                {
                    if (!isAffiliated)
                    {
                        DebugConsole.ThrowError($"Error in AbilityConditionMission \"{characterTalent.DebugIdentifier}\" - \"{missionTypeString}\" is not a valid mission type.");
                    }
                    continue;
                }

                missionTypes.Add(parsedMission);
            }

            missionType = missionTypes.ToImmutableHashSet();
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityMission { Mission: { } mission })
            {
                if (!isAffiliated) { return CheckMissionType(); }

                if (GameMain.GameSession?.Campaign?.Factions is not { } factions) { return false; }

                foreach (var (factionIdentifier, amount) in mission.ReputationRewards)
                {
                    if (amount <= 0) { continue; }
                    if (GetMatchingFaction(factionIdentifier) is { } faction &&
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
