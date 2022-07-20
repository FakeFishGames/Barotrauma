using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillPrefab
    {
        public readonly Identifier Identifier;

        public Range<float> LevelRange { get; private set; }

        /// <summary>
        /// How much this skill affects characters' hiring cost
        /// </summary>
        public readonly float PriceMultiplier;

        public bool IsPrimarySkill { get; }

        public SkillPrefab(ContentXElement element) 
        {
            Identifier = element.GetAttributeIdentifier("identifier", "");
            PriceMultiplier = element.GetAttributeFloat("pricemultiplier", 25.0f);
            var levelString = element.GetAttributeString("level", "");
            if (levelString.Contains(","))
            {
                var rangeVector2 = XMLExtensions.ParseVector2(levelString, false);
                LevelRange = new Range<float>(rangeVector2.X, rangeVector2.Y);
            }
            else
            {
                float skillLevel = float.Parse(levelString, System.Globalization.CultureInfo.InvariantCulture);
                LevelRange = new Range<float>(skillLevel, skillLevel);
            }

            IsPrimarySkill = element.GetAttributeBool("primary", false);
        }
    }
}
