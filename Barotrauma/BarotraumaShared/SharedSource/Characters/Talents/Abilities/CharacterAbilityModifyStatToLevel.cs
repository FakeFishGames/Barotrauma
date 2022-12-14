using Microsoft.Xna.Framework;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyStatToLevel : CharacterAbility
    {
        private readonly StatTypes statType;
        private readonly float statPerLevel;
        private readonly int maxLevel;
        private float lastValue = 0f;
        public override bool AllowClientSimulation => true;

        public CharacterAbilityModifyStatToLevel(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            statPerLevel = abilityElement.GetAttributeFloat("statperlevel", 0f);
            maxLevel = abilityElement.GetAttributeInt("maxlevel", int.MaxValue);
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            Character.ChangeStat(statType, -lastValue);
            if (conditionsMatched)
            {
                int level = MathHelper.Min(Character?.Info.GetCurrentLevel() ?? 0, maxLevel);
                lastValue = statPerLevel * level;
                Character.ChangeStat(statType, lastValue);
            }
            else
            {
                lastValue = 0f;
            }
        }
    }
}
