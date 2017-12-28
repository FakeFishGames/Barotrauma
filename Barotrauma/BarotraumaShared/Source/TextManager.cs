using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    static class TextManager
    {
        private static Dictionary<string, List<string>> texts;

        static TextManager()
        {
            Load(Path.Combine("Content", "Texts.xml"));            
        }

        private static void Load(string file)
        {
            texts = new Dictionary<string, List<string>>();

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;            

            foreach (XElement subElement in doc.Root.Elements())
            {
                string infoName = subElement.Name.ToString().ToLowerInvariant();
                List<string> infoList = null;
                if (!texts.TryGetValue(infoName, out infoList))
                {
                    infoList = new List<string>();
                    texts.Add(infoName, infoList);
                }

                infoList.Add(subElement.ElementInnerText());
            }
        }

        public static string Get(string textTag)
        {
            List<string> textList = null;
            if (!texts.TryGetValue(textTag.ToLowerInvariant(), out textList) || !textList.Any())
            {
                DebugConsole.ThrowError("Text \"" + textTag + "\" not found");
                return textTag;
            }

            string text = textList[Rand.Int(textList.Count)].Replace(@"\n", "\n");

            //todo: get rid of these and only do where needed?
#if CLIENT
            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                text = text.Replace("[" + inputType.ToString() + "]", GameMain.Config.KeyBind(inputType).ToString());
            }
#endif
            return text;
        }

        public static string ReplaceGenderPronouns(string text, Gender gender)
        {
            if (gender == Gender.Male)
            {
                return text.Replace("[genderpronoun]",     Get("PronounMale").ToLower())
                    .Replace("[genderpronounpossessive]",  Get("PronounPossessiveMale").ToLower())
                    .Replace("[genderpronounreflexive]",   Get("PronounReflexiveMale").ToLower())
                    .Replace("[Genderpronoun]",            Capitalize(Get("PronounMale")))
                    .Replace("[Genderpronounpossessive]",  Capitalize(Get("PronounPossessiveMale")))
                    .Replace("[Genderpronounreflexive]",   Capitalize(Get("PronounReflexiveMale")));
            }
            else
            {
                return text.Replace("[genderpronoun]",     Get("PronounFemale").ToLower())
                    .Replace("[genderpronounpossessive]",  Get("PronounPossessiveFemale").ToLower())
                    .Replace("[genderpronounreflexive]",   Get("PronounReflexiveFemale").ToLower())
                    .Replace("[Genderpronoun]",            Capitalize(Get("PronounFemale")))
                    .Replace("[Genderpronounpossessive]",  Capitalize(Get("PronounPossessiveFemale")))
                    .Replace("[Genderpronounreflexive]",   Capitalize(Get("PronounReflexiveFemale")));
            }
        }

        private static string Capitalize(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return str;
            }

            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}
