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

        public static string ReplaceGenderNouns(string text, Gender gender)
        {
            if (gender == Gender.Male)
            {
                return text.Replace("[gendernoun]", "he")
                    .Replace("[gendernounpossessive]", "his")
                    .Replace("[gendernounreflexive]", "himself")
                    .Replace("[Gendernoun]", "He")
                    .Replace("[Gendernounpossessive]", "His")
                    .Replace("[Gendernounreflexive]", "Himself");
            }
            else
            {
                return text.Replace("[gendernoun]", "she")
                    .Replace("[gendernounpossessive]", "her")
                    .Replace("[gendernounreflexive]", "herself")
                    .Replace("[Gendernoun]", "She")
                    .Replace("[Gendernounpossessive]", "Her")
                    .Replace("[Gendernounreflexive]", "Herself");
            }
        }
    }
}
