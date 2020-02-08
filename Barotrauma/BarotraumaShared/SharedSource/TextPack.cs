using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    class TextPack
    {
        public readonly string Language;

        /// <summary>
        /// The name of the language in the language this pack is written in
        /// </summary>
        public readonly string TranslatedName;

        private readonly Dictionary<string, List<string>> texts;
        
        public readonly string FilePath;

        public TextPack(string filePath)
        {
            this.FilePath = filePath;
            texts = new Dictionary<string, List<string>>();

            XDocument doc = null;
            for (int i = 0; i < 3; i++)
            {
                doc = XMLExtensions.TryLoadXml(filePath);
                if (doc != null) { break; }
                if (filePath.ToLowerInvariant() == "content/texts/englishvanilla.xml")
                {
                    //try fixing legacy EnglishVanilla path
                    string newPath = "Content/Texts/English/EnglishVanilla.xml";
                    if (System.IO.File.Exists(newPath))
                    {
                        DebugConsole.NewMessage("Content package is using the obsolete text file path \"" + filePath + "\". Attempting to load from \"" + newPath + "\"...");
                        this.FilePath = filePath = newPath;
                    }
                }
                Thread.Sleep(1000);
            }
            if (doc == null)
            {
                Language = "Unknown";
                return;
            }

            Language = doc.Root.GetAttributeString("language", "Unknown");
            TranslatedName = doc.Root.GetAttributeString("translatedname", Language);

            foreach (XElement subElement in doc.Root.Elements())
            {
                string infoName = subElement.Name.ToString().ToLowerInvariant();
                if (!texts.TryGetValue(infoName, out List<string> infoList))
                {
                    infoList = new List<string>();
                    texts.Add(infoName, infoList);
                }

                string text = subElement.ElementInnerText();
                text = text.Replace("&amp;", "&");
                text = text.Replace("&lt;", "<");
                text = text.Replace("&gt;", ">");
                text = text.Replace("&quot;", "\"");
                infoList.Add(text);
            }
        }

        public string Get(string textTag)
        {
            if (string.IsNullOrEmpty(textTag))
            {
                return null;
            }
            if (!texts.TryGetValue(textTag.ToLowerInvariant(), out List<string> textList) || !textList.Any())
            {
                return null;
            }

            string text = textList[Rand.Int(textList.Count)].Replace(@"\n", "\n");
            return text;
        }

        public List<string> GetAll(string textTag)
        {
            if (!texts.TryGetValue(textTag.ToLowerInvariant(), out List<string> textList) || !textList.Any())
            {
                return null;
            }

            return textList;
        }

        public List<KeyValuePair<string, string>> GetAllTagTextPairs()
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, List<string>> kvp in texts)
            {
                foreach (string line in kvp.Value)
                {
                    pairs.Add(new KeyValuePair<string, string>(kvp.Key, line));
                }
            }

            return pairs;
        }

#if DEBUG
        public void CheckForDuplicates(int index)
        {
            Dictionary<string, int> tagCounts = new Dictionary<string, int>();
            Dictionary<string, int> contentCounts = new Dictionary<string, int>();

            XDocument doc = XMLExtensions.TryLoadXml(FilePath);
            if (doc == null) { return; }

            foreach (XElement subElement in doc.Root.Elements())
            {
                string infoName = subElement.Name.ToString().ToLowerInvariant();
                if (!tagCounts.ContainsKey(infoName))
                {
                    tagCounts.Add(infoName, 1);
                }
                else
                {
                    tagCounts[infoName] += 1;
                }
                
                string infoContent = subElement.Value;
                if (string.IsNullOrEmpty(infoContent)) continue;
                if (!contentCounts.ContainsKey(infoContent))
                {
                    contentCounts.Add(infoContent, 1);
                }
                else
                {
                    contentCounts[infoContent] += 1;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("Language: " + Language);
            sb.AppendLine();
            sb.Append("Duplicate tags:");
            sb.AppendLine();
            sb.AppendLine();

            for (int i = 0; i < tagCounts.Keys.Count; i++)
            {
                if (tagCounts[texts.Keys.ElementAt(i)] > 1)
                {
                    sb.Append(texts.Keys.ElementAt(i) + " | Count: " + tagCounts[texts.Keys.ElementAt(i)]);
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Duplicate content:");
            sb.AppendLine();
            sb.AppendLine();

            for (int i = 0; i < contentCounts.Keys.Count; i++)
            {
                if (contentCounts[contentCounts.Keys.ElementAt(i)] > 1)
                {
                    sb.Append(contentCounts.Keys.ElementAt(i) + " | Count: " + contentCounts[contentCounts.Keys.ElementAt(i)]);
                    sb.AppendLine();
                }
            }

            System.IO.StreamWriter file = new System.IO.StreamWriter(@"duplicate_" + Language.ToLower() + "_" + index + ".txt");
            file.WriteLine(sb.ToString());
            file.Close();
        }

        public void WriteToCSV(int index)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < texts.Count; i++)
            {
                string key = texts.Keys.ElementAt(i);
                texts.TryGetValue(key, out List<string> infoList);
                
                for (int j = 0; j < infoList.Count; j++)
                {
                    sb.Append(key); // ID
                    sb.Append('*');
                    sb.Append(infoList[j]); // Original
                    sb.Append('*');
                    // Translated
                    sb.Append('*');
                    // Comments
                    sb.AppendLine();
                }
            }

            System.IO.StreamWriter file = new System.IO.StreamWriter(@"csv_" + Language.ToLower() + "_" + index + ".csv");
            file.WriteLine(sb.ToString());
            file.Close();
        }
#endif
    }
}
