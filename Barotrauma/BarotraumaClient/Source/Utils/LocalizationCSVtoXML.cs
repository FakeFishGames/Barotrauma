#if DEBUG
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    class LocalizationCSVtoXML
    {
        private static Regex csvSplit = new Regex("(?:^|,)(\"(?:[^\"])*\"|[^,]*)", RegexOptions.Compiled); // Handling commas inside data fields surrounded by ""
        private static List<int> conversationClosingIndent = new List<int>();
        private static char[] separator = new char[1] { ',' };

        private const string conversationsPath = "Content/NPCConversations";
        private const string infoTextPath = "Content/Texts";
        private const string xmlHeader = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>";

        public static void Convert(string language)
        {
            List<string> conversationFiles = new List<string>();
            List<string> infoTextFiles = new List<string>();

            language = language.CapitaliseFirstInvariant();

            foreach (string filePath in Directory.GetFiles(conversationsPath, "*.csv", SearchOption.AllDirectories))
            {
                conversationFiles.Add(filePath);
            }

            foreach (string filePath in Directory.GetFiles(infoTextPath, "*.csv", SearchOption.AllDirectories))
            {
                infoTextFiles.Add(filePath);
            }

            for (int i = 0; i < conversationFiles.Count; i++)
            {
                List<string> xmlContent = ConvertConversationsToXML(File.ReadAllLines(conversationFiles[i], Encoding.UTF8), language);
                string xmlFileFullPath = $"{conversationsPath}/NPCConversations_{language}_NEW.xml";
                File.WriteAllLines(xmlFileFullPath, xmlContent);
                DebugConsole.NewMessage("Conversation localization .xml file successfully created at: " + xmlFileFullPath);
            }

            for (int i = 0; i < infoTextFiles.Count; i++)
            {
                List<string> xmlContent = ConvertInfoTextToXML(File.ReadAllLines(infoTextFiles[i], Encoding.UTF8), language);
                string xmlFileFullPath = $"{infoTextPath}/{language}Vanilla_NEW.xml";
                File.WriteAllLines(xmlFileFullPath, xmlContent);
                DebugConsole.NewMessage("InfoText localization .xml file successfully created at: " + xmlFileFullPath);
            }
            
            if (conversationFiles.Count == 0 && infoTextFiles.Count == 0)
            {
                DebugConsole.ThrowError("No .csv files found to convert.");
            }
        }

        private static List<string> ConvertInfoTextToXML(string[] csvContent, string language)
        {
            List<string> xmlContent = new List<string>();
            xmlContent.Add(xmlHeader);

            xmlContent.Add($"<infotexts language=\"{language}\">");
            xmlContent.Add(string.Empty);

            for (int i = 0; i < csvContent.Length; i++)
            {
                csvContent[i] = csvContent[i].Trim(separator);

                if (csvContent[i].Length == 0)
                {
                    xmlContent.Add(string.Empty);
                }
                else
                {
                    string[] split = csvContent[i].Split(separator, 2);

                    if (split.Length == 2)
                    {
                        split[1] = split[1].Replace("\"", ""); // Replaces quotation marks around data that are added when exporting via excel
                        xmlContent.Add($"<{split[0]}>{split[1]}</{split[0]}>");
                    }
                    else if (split[0].Contains(".")) // An empty field
                    {
                        xmlContent.Add($"<{split[0]}><!-- No data --></{split[0]}>");
                    }
                    else // A header
                    {
                        xmlContent.Add($"<!-- {split[0]} -->");
                    }
                }
            }

            xmlContent.Add(string.Empty);
            xmlContent.Add("</infotexts>");

            return xmlContent;
        }

        private static List<string> ConvertConversationsToXML(string[] csvContent, string language)
        {
            List<string> xmlContent = new List<string>();
            xmlContent.Add(xmlHeader);

            xmlContent.Add($"<Conversations identifier=\"vanillaconversations\" Language=\"{language}\">");
            xmlContent.Add(string.Empty);

            for (int i = 0; i < csvContent.Length; i++)
            {
                string[] split = SplitCSV(csvContent[i]);

                int emptyFields = 0;

                for (int j = 0; j < split.Length; j++)
                {
                    if (split[j] == string.Empty) emptyFields++;
                }

                if (emptyFields == split.Length) // Empty line with only commas, indicates the end of the previous conversation
                {
                    HandleClosingElements(xmlContent, 0);
                    xmlContent.Add(string.Empty);
                    continue;
                }
                else if (emptyFields == split.Length - 1 && split[0] != string.Empty) // A header
                {
                    xmlContent.Add($"<!-- {split[0]} -->");
                    continue;
                }

                string speaker = split[1];
                int depthIndex = int.Parse(split[2]);
                // 3 = original line
                string line = split[4].Replace("\"", "");
                string flags = split[5].Replace("\"", "");
                string allowedJobs = split[6].Replace("\"", "");
                string speakerTags = split[7].Replace("\"", "");
                string minIntensity = split[8].Replace("\"", "");
                string maxIntensity = split[9].Replace("\"", "");

                string element =
                    $"{GetIndenting(depthIndex)}" +
                    $"<Conversation line=\"{line}\" " +
                    $"{GetVariable("speaker" ,speaker)}" +
                    $"{GetVariable("flags", flags)}" +
                    $"{GetVariable("allowedjobs", allowedJobs)}" +
                    $"{GetVariable("speakertags", speakerTags)}" +
                    $"{GetVariable("minintensity", minIntensity)}" +
                    $"{GetVariable("maxintensity", maxIntensity)}";

                bool nextIsSubConvo = false;
                int nextDepth = 999;

                if (i < csvContent.Length - 1) // Not at the end of file
                {
                    string[] nextConversationElement = csvContent[i + 1].Split(separator);

                    if (nextConversationElement[1] != string.Empty)
                    {
                        nextDepth = int.Parse(nextConversationElement[2]);
                        nextIsSubConvo = nextDepth > depthIndex;
                    }

                    if (!nextIsSubConvo)
                    {
                        xmlContent.Add(element.TrimEnd() + "/>");
                        if (nextDepth < depthIndex)
                        {
                            HandleClosingElements(xmlContent, nextDepth);
                        }
                    }
                    else
                    {
                        xmlContent.Add(element.TrimEnd() + ">");
                        conversationClosingIndent.Add(depthIndex);
                    }
                }
                else
                {
                    xmlContent.Add(element.TrimEnd() + "/>");
                }
            }

            xmlContent.Add(string.Empty);
            xmlContent.Add("</Conversations>");

            return xmlContent;
        }     

        private static void HandleClosingElements(List<string> xmlContent, int targetDepth)
        {
            if (conversationClosingIndent.Count == 0) return;

            for (int k = conversationClosingIndent.Count - 1; k >= 0; k--)
            {
                int currentIndent = conversationClosingIndent[k];
                if (currentIndent < targetDepth) break;
                xmlContent.Add($"{GetIndenting(currentIndent)}</Conversation>");
                conversationClosingIndent.RemoveAt(k);
            }
        }

        private static string[] SplitCSV(string input) // Splits the .csv with regex, leaving commas inside quotation marks intact
        {
            List<string> list = new List<string>();
            string curr = null;
            foreach (Match match in csvSplit.Matches(input))
            {
                curr = match.Value;
                if (0 == curr.Length)
                {
                    list.Add("");
                }

                list.Add(curr.TrimStart(','));
            }

            return list.ToArray();
        }

        private static string GetIndenting(int depthIndex)
        {
            string indenting = string.Empty;

            for (int i = 0; i < depthIndex; i++)
            {
                indenting += "\t";
            }

            return indenting;
        }

        private static string GetVariable(string name, string value)
        {
            if (value == string.Empty)
            {
                return string.Empty;
            }
            else
            {
                return $"{name}=\"{value}\" ";
            }
        }
    }
}
#endif
