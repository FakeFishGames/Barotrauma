#if DEBUG
using System;
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
        private static char[] separator = new char[1] { '|' };

        private const string conversationsPath = "Content/NPCConversations";
        private const string infoTextPath = "Content/Texts";
        private const string xmlHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

        private static string[,] translatedLanguageNames = new string[11, 2] { { "English", "English" }, { "French", "Français" }, { "German", "Deutsch" }, 
            { "Russian", "Русский" }, { "Brazilian Portuguese", "Português brasileiro" }, { "Simplified Chinese", "中文(简体)" }, { "Traditional Chinese", "中文(繁體)" },
            { "CastilianSpanish", "Castellano" }, { "LatinamericanSpanish", "Español Latinoamericano" }, { "Polish", "Polski" }, { "Turkish", "Türkçe" } };

        public static void Convert()
        {
            if (TextManager.Language != "English")
            {
                DebugConsole.ThrowError("Use the english localization when converting .csv to allow copying values");
                return;
            }

            List<string> conversationFiles = new List<string>();
            List<string> infoTextFiles = new List<string>();
            
            for (int i = 0; i < translatedLanguageNames.GetUpperBound(0) + 1; i++)
            {
                string language = translatedLanguageNames[i, 0];
                string languageNoWhitespace = language.RemoveWhitespace();

                if (Directory.Exists(conversationsPath + $"/{languageNoWhitespace}"))
                {
                    string[] conversationFileArray = Directory.GetFiles(conversationsPath + $"/{languageNoWhitespace}", "*.csv", SearchOption.AllDirectories);

                    if (conversationFileArray != null)
                    {
                        foreach (string filePath in conversationFileArray)
                        {
                            conversationFiles.Add(filePath);
                        }
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Directory at: " + conversationsPath + $"/{languageNoWhitespace} does not exist!");
                }

                if (Directory.Exists(infoTextPath + $"/{languageNoWhitespace}"))
                {
                    string[] infoTextFileArray = Directory.GetFiles(infoTextPath + $"/{languageNoWhitespace}", "*.csv", SearchOption.AllDirectories);

                    if (infoTextFileArray != null)
                    {
                        foreach (string filePath in infoTextFileArray)
                        {
                            infoTextFiles.Add(filePath);
                        }
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Directory at: " + infoTextPath + $"/{languageNoWhitespace} does not exist!");
                }

                for (int j = 0; j < conversationFiles.Count; j++)
                {
                    List<string> xmlContent = ConvertConversationsToXML(File.ReadAllLines(conversationFiles[j], Encoding.UTF8), language);
                    if (xmlContent == null)
                    {
                        DebugConsole.ThrowError("NPCConversation Localization .csv to .xml conversion failed for: " + conversationFiles[j]);
                        continue;
                    }
                    string xmlFileFullPath = $"{conversationsPath}/{languageNoWhitespace}/NpcConversations_{languageNoWhitespace}_NEW.xml";
                    File.WriteAllLines(xmlFileFullPath, xmlContent);
                    DebugConsole.NewMessage("Conversation localization .xml file successfully created at: " + xmlFileFullPath);
                }

                for (int j = 0; j < infoTextFiles.Count; j++)
                {
                    List<string> xmlContent = ConvertInfoTextToXML(File.ReadAllLines(infoTextFiles[j], Encoding.UTF8), language);
                    if (xmlContent == null)
                    {
                        DebugConsole.ThrowError("InfoText Localization .csv to .xml conversion failed for: " + infoTextFiles[j]);
                        continue;
                    }
                    string xmlFileFullPath = $"{infoTextPath}/{languageNoWhitespace}/{languageNoWhitespace}Vanilla_NEW.xml";
                    File.WriteAllLines(xmlFileFullPath, xmlContent);
                    DebugConsole.NewMessage("InfoText localization .xml file successfully created at: " + xmlFileFullPath);
                }

                if (conversationFiles.Count == 0 && infoTextFiles.Count == 0)
                {
                    DebugConsole.ThrowError("No .csv files found to convert for: " + language);
                    continue;
                }

                conversationFiles.Clear();
                infoTextFiles.Clear();
            }
        }

        private static List<string> ConvertInfoTextToXML(string[] csvContent, string language)
        {
            List<string> xmlContent = new List<string>();
            xmlContent.Add(xmlHeader);

            string translatedName = GetTranslatedName(language);
            bool nowhitespace = TextManager.IsCJK(translatedName);

            xmlContent.Add($"<infotexts language=\"{language}\" nowhitespace=\"{nowhitespace.ToString().ToLower()}\" translatedname=\"{translatedName}\">");

            for (int i = 1; i < csvContent.Length; i++) // Start at one to ignore header
            {
                csvContent[i] = csvContent[i].Trim(separator);

                if (csvContent[i].Length == 0)
                {
                    xmlContent.Add(string.Empty);
                }
                else
                {
                    string[] split = csvContent[i].Split(separator, 3);

                    if (split.Length >= 2) // >= 2 = has comments, -> ignored
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

        private static string GetTranslatedName(string language)
        {
            for (int i = 0; i < translatedLanguageNames.Length; i++)
            {
                if (translatedLanguageNames[i, 0] == language) return translatedLanguageNames[i, 1];
            }

            DebugConsole.ThrowError("No translated language name found for " + language);
            return string.Empty;
        }

        private static List<string> ConvertConversationsToXML(string[] csvContent, string language)
        {
            List<string> xmlContent = new List<string>();
            xmlContent.Add(xmlHeader);

            string translatedName = GetTranslatedName(language);
            bool nowhitespace = TextManager.IsCJK(translatedName);

            xmlContent.Add($"<Conversations identifier=\"vanillaconversations\" Language=\"{language}\" nowhitespace=\"{nowhitespace}\">");
            xmlContent.Add(string.Empty);
            xmlContent.Add("<!-- Personality traits -->");

            int traitStart = -1;
            for (int i = 0; i < csvContent.Length; i++)
            {
                if (csvContent[i].StartsWith("Personality"))
                {
                    traitStart = i + 1;
                    break;
                }
            }

            int conversationStart = -1;
            for (int i = 0; i < csvContent.Length; i++)
            {
                if (csvContent[i].StartsWith("Generic"))
                {
                    conversationStart = i;
                    break;
                }
            }

            if (traitStart == -1)
            {
                DebugConsole.ThrowError("Invalid formatting of NPCConversations, no traits found!");
                return null;
            }

            //DebugConsole.NewMessage("Count: " + NPCPersonalityTrait.List.Count);
            for (int i = 0; i < NPCPersonalityTrait.List.Count; i++) // Traits
            {
                //string[] split = SplitCSV(csvContent[traitStart + i].Trim(separator));
                string[] split = csvContent[traitStart + i].Split(separator);
                xmlContent.Add(
                    $"<PersonalityTrait " +
                    $"{GetVariable("name", split[1])}" +
                    $"{GetVariable("alloweddialogtags", string.Join(",", NPCPersonalityTrait.List[i].AllowedDialogTags))}" +
                    $"{GetVariable("commonness", NPCPersonalityTrait.List[i].Commonness.ToString())}/>");
            }

            xmlContent.Add(string.Empty);

            for (int i = conversationStart; i < csvContent.Length; i++) // Conversations
            {
                string[] split = csvContent[i].Split(separator);

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
                string line = split[3].Replace("\"", "");
                string flags = split[4].Replace("\"", "");
                string allowedJobs = split[5].Replace("\"", "");
                string speakerTags = split[6].Replace("\"", "");
                string minIntensity = split[7].Replace("\"", "").Replace(",", ".");
                string maxIntensity = split[8].Replace("\"", "").Replace(",", ".");

                string element =
                    $"{GetIndenting(depthIndex)}" +
                    $"<Conversation line=\"{line}\" " +
                    $"{GetVariable("speaker", speaker)}" +
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

                list.Add(curr.TrimStart(separator));
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
