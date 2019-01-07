using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class TextPack
    {
        public readonly string Language;

        private Dictionary<string, List<string>> texts;

        public TextPack(string filePath)
        {
            texts = new Dictionary<string, List<string>>();

            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return;

            Language = doc.Root.GetAttributeString("language", "Unknown");

            foreach (XElement subElement in doc.Root.Elements())
            {
                string infoName = subElement.Name.ToString().ToLowerInvariant();
                if (!texts.TryGetValue(infoName, out List<string> infoList))
                {
                    infoList = new List<string>();
                    texts.Add(infoName, infoList);
                }

                infoList.Add(subElement.ElementInnerText());
            }
        }

        public string Get(string textTag)
        {
            if (!texts.TryGetValue(textTag.ToLowerInvariant(), out List<string> textList) || !textList.Any())
            {
                return null;
            }

            string text = textList[Rand.Int(textList.Count)].Replace(@"\n", "\n");
            return text;
        }
    }
}
