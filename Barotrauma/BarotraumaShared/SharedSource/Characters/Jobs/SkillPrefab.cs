using System.Globalization;

namespace Barotrauma
{
    class SkillPrefab
    {
        public readonly Identifier Identifier;

        private readonly Range<float> levelRange;
        private readonly Range<float> levelRangePvP;

        /// <summary>
        /// How much this skill affects characters' hiring cost
        /// </summary>
        public readonly float PriceMultiplier;

        public bool IsPrimarySkill { get; }

        public SkillPrefab(ContentXElement element) 
        {
            Identifier = element.GetAttributeIdentifier("identifier", "");
            PriceMultiplier = element.GetAttributeFloat("pricemultiplier", 15.0f);
            levelRange = GetSkillRange("level", element, defaultValue: new Range<float>(0, 0));
            levelRangePvP = GetSkillRange("pvplevel", element, defaultValue: levelRange);
            IsPrimarySkill = element.GetAttributeBool("primary", false);

            static Range<float> GetSkillRange(string attributeName, ContentXElement element, Range<float> defaultValue)
            {
                string levelString = element.GetAttributeString(attributeName, string.Empty);
                if (levelString.Contains(','))
                {
                    var rangeVector2 = XMLExtensions.ParseVector2(levelString, false);
                    return new Range<float>(rangeVector2.X, rangeVector2.Y);
                }
                else if (float.TryParse(levelString, NumberStyles.Any, CultureInfo.InvariantCulture, out float skillLevel))
                {
                    return new Range<float>(skillLevel, skillLevel);
                }
                else
                {
                    return defaultValue;
                }
            }
        }

        public Range<float> GetLevelRange(bool isPvP)
        {
            return isPvP ? levelRangePvP : levelRange;
        }
    }
}
