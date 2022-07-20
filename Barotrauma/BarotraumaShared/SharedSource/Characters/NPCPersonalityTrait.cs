using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCPersonalityTrait
    {
        public readonly Identifier Name;

        public readonly List<string> AllowedDialogTags;

        private readonly float commonness;
        public float Commonness
        {
            get { return commonness; }
        }

        public static IEnumerable<NPCPersonalityTrait> GetAll(LanguageIdentifier language)
        {
            if (language != TextManager.DefaultLanguage && !NPCConversationCollection.Collections.ContainsKey(language))
            {
                DebugConsole.AddWarning($"Could not find NPC personality traits for the language \"{language}\". Using \"{TextManager.DefaultLanguage}\" instead..");
                language = TextManager.DefaultLanguage;
            }
            return NPCConversationCollection.Collections[language]
                .SelectMany(cc => cc.PersonalityTraits.Values);
        }

        public static NPCPersonalityTrait Get(LanguageIdentifier language, Identifier traitName)
        {
            if (language != TextManager.DefaultLanguage && !NPCConversationCollection.Collections.ContainsKey(language))
            {
                DebugConsole.AddWarning($"Could not find NPC personality traits for the language \"{language}\". Using \"{TextManager.DefaultLanguage}\" instead..");
                language = TextManager.DefaultLanguage;
            }
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
