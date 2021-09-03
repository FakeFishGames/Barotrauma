using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionMission : AbilityConditionData
    {
        private readonly MissionType missionType;
        public AbilityConditionMission(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            string missionTypeString = conditionElement.GetAttributeString("missiontype", "None");
            if (!Enum.TryParse(missionTypeString, out missionType))
            {
                DebugConsole.ThrowError("Error in AbilityConditionMission \"" + characterTalent.DebugIdentifier + "\" - \"" + missionTypeString + "\" is not a valid mission type.");
                return;
            }
            if (missionType == MissionType.None)
            {
                DebugConsole.ThrowError("Error in AbilityConditionMission \"" + characterTalent.DebugIdentifier + "\" - mission type cannot be none.");
                return;
            }
        }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is IAbilityMission abilityMission)
            {
                return abilityMission.Mission.Prefab.Type == missionType;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(IAbilityMission));
                return false;
            }
        }
    }
}
