using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    static class InfoTextManager
    {

        private static Dictionary<string, List<string>> infoTexts;

        static InfoTextManager()
        {
            LoadInfoTexts(Path.Combine("Content", "InfoTexts.xml"));            
        }


        private static void LoadInfoTexts(string file)
        {
            infoTexts = new Dictionary<string, List<string>>();

            XDocument doc = ToolBox.TryLoadXml(file);
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

        public static string GetInfoText(string infoName)
        {
            List<string> infoList = null;
            if (!infoTexts.TryGetValue(infoName.ToLowerInvariant(), out infoList) || !infoList.Any())
            {
#if DEBUG
                return "Info text \"" + infoName + "\" not found";
#else
                return "";
#endif
            }

            string text = infoList[Rand.Int(infoList.Count)];

            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                text = text.Replace("[" + inputType.ToString() + "]", GameMain.Config.KeyBind(inputType).ToString());
            }

            if (Submarine.MainSub != null) text = text.Replace("[sub]", Submarine.MainSub.Name);
            if (GameMain.GameSession != null && GameMain.GameSession.StartLocation != null)
            {
                text = text.Replace("[location]", GameMain.GameSession.StartLocation.Name);
            }

            return text;
        }
    }
}
