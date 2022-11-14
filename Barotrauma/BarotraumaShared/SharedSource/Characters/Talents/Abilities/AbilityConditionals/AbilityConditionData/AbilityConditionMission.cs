using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionMission : AbilityConditionData
    {
        private readonly ImmutableHashSet<MissionType> missionType;
        private readonly ImmutableHashSet<Identifier> factions;

        public AbilityConditionMission(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            string[] missionTypeStrings = conditionElement.GetAttributeStringArray("missiontype", new []{ "None" })!;
            HashSet<MissionType> missionTypes = new HashSet<MissionType>();
            factions = conditionElement.GetAttributeIdentifierImmutableHashSet("faction", ImmutableHashSet<Identifier>.Empty);

            foreach (string missionTypeString in missionTypeStrings)
            {
                if (!Enum.TryParse(missionTypeString, out MissionType parsedMission) || parsedMission is MissionType.None)
                {
                    if (factions.IsEmpty)
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
                if (factions.Any())
                {
                    // FIXME there's probably a better way to check the faction affiliated with the mission later
                    return mission.ReputationRewards.Keys.Any(factionIdentifier => factions.Contains(factionIdentifier));
                }

                return missionType.Contains(mission.Prefab.Type);
            }

            LogAbilityConditionError(abilityObject, typeof(IAbilityMission));
            return false;
        }
    }
}
