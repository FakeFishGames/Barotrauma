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
            foreach (string missionTypeString in missionTypeStrings)
            {
                if (!Enum.TryParse(missionTypeString, out MissionType parsedMission) || parsedMission is MissionType.None)
                {
                    DebugConsole.ThrowError($"Error in AbilityConditionMission \"{characterTalent.DebugIdentifier}\" - \"{missionTypeString}\" is not a valid mission type.");
                    return;
                }

                missionTypes.Add(parsedMission);
            }

            missionType = missionTypes.ToImmutableHashSet();
            isAffiliated = conditionElement.GetAttributeBool("isaffiliated", false);
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityMission { Mission: { } mission })
            {
                if (isAffiliated)
                {
                    if (GameMain.GameSession?.Campaign?.Factions is not { } factions) { return false; }

                    // FIXME there's probably a better way to check the faction affiliated with the mission later
                    foreach (Identifier factionIdentifier in mission.ReputationRewards.Keys)
                    {
                        if (factions.Where(faction => factionIdentifier == faction.Prefab.Identifier).Any(static faction => faction.GetPlayerAffiliationStatus() != FactionAffiliation.Affiliated))
                        {
                            return false;
                        }
                    }
                }

                return missionType.Contains(mission.Prefab.Type);
            }

            LogAbilityConditionError(abilityObject, typeof(IAbilityMission));
            return false;
        }
    }
}
