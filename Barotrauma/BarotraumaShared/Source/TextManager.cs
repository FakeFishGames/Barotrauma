using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    static class TextManager
    {
        private static Dictionary<string, List<string>> infoTexts;

        static TextManager()
        {
            Load(Path.Combine("Content", "Texts.xml"));            
        }

        private static void Load(string file)
        {
            infoTexts = new Dictionary<string, List<string>>();

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;            

            foreach (XElement subElement in doc.Root.Elements())
            {
                string infoName = subElement.Name.ToString().ToLowerInvariant();
                List<string> infoList = null;
                if (!infoTexts.TryGetValue(infoName, out infoList))
                {
                    infoList = new List<string>();
                    infoTexts.Add(infoName, infoList);
                }

                infoList.Add(subElement.ElementInnerText());
            }
        }

        public static string Get(string infoName)
        {
            List<string> infoList = null;
            if (!infoTexts.TryGetValue(infoName.ToLowerInvariant(), out infoList) || !infoList.Any())
            {
                DebugConsole.ThrowError("Info text \"" + infoName + "\" not found");
                return infoName;
            }

            string text = infoList[Rand.Int(infoList.Count)];

            //todo: get rid of these and only do where needed?
#if CLIENT
            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                text = text.Replace("[" + inputType.ToString() + "]", GameMain.Config.KeyBind(inputType).ToString());
            }
#endif
            return text;
        }
    }
}
