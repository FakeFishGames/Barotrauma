using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class SkillPrefab
    {
        string name;

        string description;

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
