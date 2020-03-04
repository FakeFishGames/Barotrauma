using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCPersonalityTrait
    {
        private static List<NPCPersonalityTrait> list = new List<NPCPersonalityTrait>();
        public static List<NPCPersonalityTrait> List
        {
            get { return list; }
        }

        public readonly string FilePath;

        public readonly string Name;

        public readonly List<string> AllowedDialogTags;

        private float commonness;
        public float Commonness
        {
            get { return commonness; }
        }

        public NPCPersonalityTrait(XElement element, string filePath)
        {
            FilePath = filePath;
            Name = element.GetAttributeString("name", "");
            AllowedDialogTags = new List<string>(element.GetAttributeStringArray("alloweddialogtags", new string[0]));
            commonness = element.GetAttributeFloat("commonness", 1.0f);

            list.Add(this);
        }

        public static NPCPersonalityTrait GetRandom(string seed)
        {
            var rand = new MTRandom(ToolBox.StringToInt(seed));
            return ToolBox.SelectWeightedRandom(list, list.Select(t => t.commonness).ToList(), rand);
        }

    }
}
