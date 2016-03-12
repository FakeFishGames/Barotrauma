using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillPrefab
    {
        private string name;

        private string description;

        private Vector2 levelRange;

        public string Name
        {
            get { return name; }
        }

        public string Description
        {
            get { return description; }
        }

        public Vector2 LevelRange
        {
            get { return levelRange; }
        }

        public SkillPrefab(XElement element) 
        {
            name = ToolBox.GetAttributeString(element, "name", "");
            
            var levelString = ToolBox.GetAttributeString(element, "level", "");
            if (levelString.Contains(","))
            {
                levelRange = ToolBox.ParseToVector2(levelString, false);
            }
            else
            {
                float skillLevel = float.Parse(levelString, System.Globalization.CultureInfo.InvariantCulture);
                levelRange = new Vector2(skillLevel, skillLevel);
            }
        }


    }
}
