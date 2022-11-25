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

                    foreach (var (factionIdentifier, amount) in mission.ReputationRewards)
                    {
                        if (amount <= 0) { continue; }

                        Faction faction = factions.FirstOrDefault(faction => factionIdentifier == faction.Prefab.Identifier);

                        if (faction?.GetPlayerAffiliationStatus() is FactionAffiliation.Affiliated)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                return missionType.Contains(mission.Prefab.Type);
            }

            LogAbilityConditionError(abilityObject, typeof(IAbilityMission));
            return false;
        }
    }
}
