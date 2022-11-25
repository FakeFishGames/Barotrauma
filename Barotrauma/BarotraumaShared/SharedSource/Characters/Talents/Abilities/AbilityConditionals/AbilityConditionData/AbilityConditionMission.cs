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
                    if (GameMain.GameSession?.Campaign?.Factions is not { } factions) { return false; }

                    foreach (var (factionIdentifier, amount) in mission.ReputationRewards)
                    {
                        if (amount <= 0) { continue; }
                        if (factions.FirstOrDefault(faction => factionIdentifier == faction.Prefab.Identifier) is Faction faction &&
                            Faction.GetPlayerAffiliationStatus(faction) is FactionAffiliation.Positive)
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
