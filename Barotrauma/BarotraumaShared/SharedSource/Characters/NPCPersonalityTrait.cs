using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCPersonalityTrait
    {
        public readonly Identifier Name;

        public readonly List<string> AllowedDialogTags;

        private float commonness;
        public float Commonness
        {
            get { return commonness; }
        }

        public static IEnumerable<NPCPersonalityTrait> GetAll(LanguageIdentifier language)
        {
            return NPCConversationCollection.Collections[language]
                .SelectMany(cc => cc.PersonalityTraits.Values);
        }

        public static NPCPersonalityTrait Get(LanguageIdentifier language, Identifier traitName)
        {
            return NPCConversationCollection.Collections[language]
                .FirstOrDefault(cc => cc.PersonalityTraits.ContainsKey(traitName))
                .PersonalityTraits[traitName];
        }

        public NPCPersonalityTrait(XElement element)
        {
            Name = element.GetAttributeIdentifier("name", "");
            AllowedDialogTags = new List<string>(element.GetAttributeStringArray("alloweddialogtags", Array.Empty<string>()));
            commonness = element.GetAttributeFloat("commonness", 1.0f);
        }

        public static NPCPersonalityTrait GetRandom(string seed)
        {
            #warning TODO: implement NPCPersonality content type and revise this for determinism
            var rand = new MTRandom(ToolBox.StringToInt(seed));
            var list = GetAll(GameSettings.CurrentConfig.Language);
            return ToolBox.SelectWeightedRandom(list, t => t.commonness, rand);
        }

    }
}
