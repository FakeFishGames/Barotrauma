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

        public readonly string Name;

        public readonly List<string> AllowedDialogTags;

        private float commonness;

        public NPCPersonalityTrait(XElement element)
        {
            Name = element.GetAttributeString("name", "");
            //TODO: use GetAttributeStringArray
            AllowedDialogTags = new List<string>();
            string allowedDialogTagsStr = element.GetAttributeString("alloweddialogtags", "");
            foreach (string allowedDialogTag in allowedDialogTagsStr.Split(','))
            {
                AllowedDialogTags.Add(allowedDialogTag.Trim());
            }

            commonness = element.GetAttributeFloat("commonness", 1.0f);

            list.Add(this);
        }

        public static NPCPersonalityTrait GetRandom(string seed)
        {
            var rand = new MTRandom(ToolBox.StringToInt(seed));

            float totalCommonness = list.Sum(t => t.commonness);
            float randomNumber = (float)(rand.NextDouble() * totalCommonness);
            foreach (NPCPersonalityTrait trait in list)
            {
                if (randomNumber <= trait.commonness)
                {
                    return trait;
                }

                randomNumber -= trait.commonness;
            }
            return null;
        }

    }
}
