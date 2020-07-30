using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillPrefab
    {
        public readonly string Identifier;

        public Vector2 LevelRange { get; private set; }

        /// <summary>
        /// How much this skill affects characters' hiring cost
        /// </summary>
        public readonly float PriceMultiplier;

        public bool IsPrimarySkill { get; }

        public SkillPrefab(XElement element) 
        {
            Identifier = element.GetAttributeString("identifier", "");
            PriceMultiplier = element.GetAttributeFloat("pricemultiplier", 25.0f);
            var levelString = element.GetAttributeString("level", "");
            if (levelString.Contains(","))
            {
                LevelRange = XMLExtensions.ParseVector2(levelString, false);
            }
            else
            {
                float skillLevel = float.Parse(levelString, System.Globalization.CultureInfo.InvariantCulture);
                LevelRange = new Vector2(skillLevel, skillLevel);
            }

            IsPrimarySkill = element.GetAttributeBool("primary", false);
        }
    }
}
