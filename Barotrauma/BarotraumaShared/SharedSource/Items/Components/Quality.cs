using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Quality : ItemComponent
    {
        public const int MaxQuality = 3;

        public enum StatType
        {
            Condition,
            ExplosionRadius,
            ExplosionDamage,
            RepairSpeed,
            RepairToolStructureRepairMultiplier,
            RepairToolStructureDamageMultiplier,
            RepairToolDeattachTimeMultiplier,
            FirepowerMultiplier,
            StrikingPowerMultiplier,
            StrikingSpeedMultiplier,
            FiringRateMultiplier
        }

        private readonly Dictionary<StatType, float> statValues = new Dictionary<StatType, float>();

        private int qualityLevel;

        [Editable(MinValueInt = 0, MaxValueInt = MaxQuality), Serialize(0, IsPropertySaveable.Yes)]
        public int QualityLevel
        {
            get { return qualityLevel; }
            set 
            {
                if (value == qualityLevel) { return; }

                bool wasInFullCondition = item.IsFullCondition;
                qualityLevel = MathHelper.Clamp(value, 0, MaxQuality);
                item.RecalculateConditionValues();
                //set the condition to the new max condition
                if (wasInFullCondition && statValues.ContainsKey(StatType.Condition))
                {
                    item.Condition = item.MaxCondition;
                }
            }
        }

        public Quality(Item item, ContentXElement element) : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "stattype":
                    case "statvalue":
                    case "qualitystat":
                        string statTypeString = subElement.GetAttributeString("stattype", "");
                        if (!Enum.TryParse(statTypeString, true, out StatType statType))
                        {
                            DebugConsole.ThrowError("Invalid stat type type \"" + statTypeString + "\" in item (" + ((MapEntity)item).Prefab.Identifier + ")");
                        }
                        float statValue = subElement.GetAttributeFloat("value", 0f);
                        statValues.TryAdd(statType, statValue);                        
                        break;
                }
            }
        }

        public float GetValue(StatType statType)
        {
            if (!statValues.ContainsKey(statType)) { return 0.0f; }
            return statValues[statType] * qualityLevel;
        }

        /// <summary>
        /// Get a random quality for an item spawning in some sub, taking into account the type of the submarine and the difficulty of the current level 
        /// (high-quality items become more common as difficulty increases)
        /// </summary>
        public static int GetSpawnedItemQuality(Submarine submarine, Level level, Rand.RandSync randSync = Rand.RandSync.ServerAndClient)
        {               
            if (submarine?.Info == null || level == null || submarine.Info.Type == SubmarineType.Player) { return 0; }

            float difficultyFactor = MathHelper.Clamp(level.Difficulty, 0.0f, level.LevelData.Biome.ActualMaxDifficulty / 100.0f);

            if (level.Type == LevelData.LevelType.Outpost && 
                level.StartLocation?.Type?.OutpostTeam == CharacterTeamType.FriendlyNPC)
            {
                //no high-quality spawns in friendly outposts
                difficultyFactor = 0.0f;
            }

            return ToolBox.SelectWeightedRandom(Enumerable.Range(0, MaxQuality + 1), q => GetCommonness(q, difficultyFactor), randSync);

            static float GetCommonness(int quality, float difficultyFactor)
            {
                return quality switch
                {
                    0 => 1,
                    1 => MathHelper.Lerp(0.0f, 1f, difficultyFactor),
                    2 => MathHelper.Lerp(0.0f, 1f, Math.Max(difficultyFactor-0.15f, 0f)), //15 difficulty transition to next biome - unlock Excellent loot
                    3 => MathHelper.Lerp(0.0f, 1f, Math.Max(difficultyFactor-0.35f, 0f)), //35 difficulty transition to next biome - unlock Masterwork loot
                    _ => 0.0f,
                };
            }
        }
    }
}
