#if DEBUG
using Barotrauma.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class LocalizationCSVtoXML
    {
        private static readonly List<int> conversationClosingIndent = new List<int>();
        private static readonly char[] separator = new char[1] { '|' };

        private const string conversationsPath = "Content/NPCConversations";
        private const string infoTextPath = "Content/Texts";
        private const string xmlHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

        private static readonly string[,] translatedLanguageNames = new string[13, 2] { { "English", "English" }, { "French", "Français" }, { "German", "Deutsch" }, 
            { "Russian", "Русский" }, { "Brazilian Portuguese", "Português brasileiro" }, { "Simplified Chinese", "中文(简体)" }, { "Traditional Chinese", "中文(繁體)" },
            { "Castilian Spanish", "Castellano" }, { "Latinamerican Spanish", "Español Latinoamericano" }, { "Polish", "Polski" }, { "Turkish", "Türkçe" },
            { "Japanese", "日本語" }, { "Korean", "한국어" } };

        public static void ConvertMasterLocalizationKit(string outputTextsDirectory, string outputConversationsDirectory, bool convertConversations)
        {
            List<string> languages = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                string textFilePath;
                string outputFileName;
                switch (i)
                {
                    case 0:
                        textFilePath = Path.Combine(infoTextPath, "Texts.csv");
                        outputFileName = "Vanilla.xml";
                        break;
                    case 1:
                        textFilePath = Path.Combine(infoTextPath, "EditorTexts.csv");
                        outputFileName = "VanillaEditorTexts.xml";
                        break;
                    default:
                        throw new NotImplementedException();
                }

                Dictionary<string, List<string>> xmlContent;
                try
                {
                    xmlContent = ConvertInfoTextToXML(File.ReadAllLines(textFilePath, Encoding.UTF8));
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("InfoText Localization .csv to .xml conversion failed for: " + textFilePath, e);
                    return;
                }
                if (xmlContent == null)
                {
                    DebugConsole.ThrowError("InfoText Localization .csv to .xml conversion failed for: " + textFilePath);
                    return;
                }
                foreach (string language in xmlContent.Keys)
                {
                    languages.Add(language);
                    string languageNoWhitespace = language.Replace(" ", "");
                    string xmlFileFullPath = Path.Combine(outputTextsDirectory, $"{languageNoWhitespace}/{languageNoWhitespace}{outputFileName}");
                    File.WriteAllLines(xmlFileFullPath, xmlContent[language], Encoding.UTF8);
                    DebugConsole.NewMessage("InfoText localization .xml file successfully created at: " + xmlFileFullPath);
                }
            }

            if (convertConversations)
            {
                string conversationFilePath = Path.Combine(infoTextPath, "NPCConversations.csv");
                var conversationLinesAll = File.ReadAllLines(conversationFilePath, Encoding.UTF8);
                foreach (string language in languages)
                {
                    List<string> convXmlContent = ConvertConversationsToXML(conversationLinesAll, language);
                    if (convXmlContent == null)
                    {
                        DebugConsole.ThrowError("NPCConversation Localization .csv to .xml conversion failed for: " + language);
                        continue;
                    }
                    string languageNoWhitespace = language.Replace(" ", "");
                    string xmlFileFullPath = Path.Combine(outputConversationsDirectory, languageNoWhitespace, $"NpcConversations_{languageNoWhitespace}.xml");
                    File.WriteAllLines(xmlFileFullPath, convXmlContent, Encoding.UTF8);
                    DebugConsole.NewMessage("Conversation localization .xml file successfully created at: " + xmlFileFullPath);
                }
            }
        }

        [Obsolete]
        public static void ConvertIndividualFiles()
        {
            if (GameSettings.CurrentConfig.Language != TextManager.DefaultLanguage)
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
                    IEnumerable<string> conversationFileArray = Directory.GetFiles(conversationsPath + $"/{languageNoWhitespace}", "*.csv", System.IO.SearchOption.AllDirectories);

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
                    IEnumerable<string> infoTextFileArray = Directory.GetFiles(infoTextPath + $"/{languageNoWhitespace}", "*.csv", System.IO.SearchOption.AllDirectories);

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
                    string xmlFileFullPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}/NpcConversations_{languageNoWhitespace}.xml";
                    File.WriteAllLines(xmlFileFullPath, xmlContent, Encoding.UTF8);
                    DebugConsole.NewMessage("Conversation localization .xml file successfully created at: " + xmlFileFullPath);
                }

                for (int j = 0; j < infoTextFiles.Count; j++)
                {
                    List<string> xmlContent;
                    try
                    {
                        xmlContent = ConvertInfoTextToXML(File.ReadAllLines(infoTextFiles[j], Encoding.UTF8), language);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("InfoText Localization .csv to .xml conversion failed for: " + infoTextFiles[j], e);
                        continue;
                    }
                    if (xmlContent == null)
                    {
                        DebugConsole.ThrowError("InfoText Localization .csv to .xml conversion failed for: " + infoTextFiles[j]);
                        continue;
                    }
                    string xmlFileFullPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}/{languageNoWhitespace}Vanilla.xml";
                    File.WriteAllLines(xmlFileFullPath, xmlContent, Encoding.UTF8);
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

        private static Dictionary<string, List<string>> ConvertInfoTextToXML(string[] csvContent)
        {
            Dictionary<string, List<string>> xmlContentByLanguage = new Dictionary<string, List<string>>();

            //get all the languages from the header row
            string headerRow = csvContent[0];
            var headerContent = headerRow.Split(separator);
            for (int i = 0; i < headerContent.Length; i++)
            {
                string languageName = headerContent[i];
                if (languageName.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
                    languageName.Equals("comments", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string translatedName = GetTranslatedName(languageName);
                bool nowhitespace = TextManager.IsCJK(translatedName);
                List<string> xmlContent = new List<string>()
                {
                    xmlHeader,
                    $"<infotexts language=\"{languageName}\" nowhitespace=\"{nowhitespace.ToString().ToLower()}\" translatedname=\"{translatedName}\">"
                };
                xmlContentByLanguage.Add(headerContent[i], xmlContent);
            }

            for (int row = 1; row < csvContent.Length; row++) // Start at one to ignore header
            {
                if (!xmlContentByLanguage.Values.All(values => values.Count == xmlContentByLanguage["English"].Count))
                {
                    throw new Exception($"Error while converting csv to xml: mismatching number of texts on line {row-1} ({csvContent[row - 1]}). Check that there's no extra newlines, separators or missing lines in the csv file.");
                }

                if (csvContent[row].Length == 0)
                {
                    AddToAllLanguages(string.Empty);
                }
                else
                {
                    string[] split = csvContent[row].Split(separator);

                    if (split.Length < xmlContentByLanguage.Count)
                    {
                        throw new Exception($"Error while converting csv to xml: not enough values on line {row} ({csvContent[row]}). Check that there's no extra newlines, separators or missing lines in the csv file.");
                    }

                    if (split.Length > 1) // Localization data
                    {
                        //all values empty = an empty line
                        if (split.All(s => s.IsNullOrEmpty()))
                        {
                            AddToAllLanguages(string.Empty);
                        }
                        //value is empty in all languages
                        else if (!split[0].IsNullOrEmpty() && split.Skip(2).All(s => s.IsNullOrEmpty()))
                        {
                            //first line is all lower-case and contains dot, assume it's an empty value
                            if (split[0].Contains(".") && !split[0].Any(char.IsUpper))
                            {
                                AddToAllLanguages($"<{split[0]}></{split[0]}>");
                            }
                            //otherwise assume it's a comment
                            else
                            {
                                AddToAllLanguages($"<!-- {split[0]} -->");
                            }
                        }
                        else
                        {
                            for (int j = 0; j < split.Length; j++)
                            {
                                string languageName = headerContent[j];
                                if (languageName.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
                                    languageName.Equals("comments", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                split[j] = split[j].Replace(" & ", " &amp; ");
                                xmlContentByLanguage[languageName].Add($"<{split[0]}>{split[j]}</{split[0]}>");
                            }
                        }
                    }
                    else // A header/comment
                    {
                        AddToAllLanguages($"<!-- {split[0]} -->");
                    }
                }
            }

            AddToAllLanguages(string.Empty);
            AddToAllLanguages("</infotexts>");

            void AddToAllLanguages(string str)
            {
                foreach (var xmlContent in xmlContentByLanguage.Values)
                {
                    xmlContent.Add(str);
                }
            }

            return xmlContentByLanguage;
        }

        [Obsolete]
        private static List<string> ConvertInfoTextToXML(string[] csvContent, string language)
        {
            List<string> xmlContent = new List<string>
            {
                xmlHeader
            };

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

                    if (split.Length >= 2) // Localization data
                    {
                        split[1] = split[1].Replace(" & ", " &amp; ");
                        xmlContent.Add($"<{split[0]}>{split[1]}</{split[0]}>");
                    }
                    else if (split[0].Contains(".") && !split[0].Any(char.IsUpper)) // An empty field
                    {
                        xmlContent.Add($"<{split[0]}></{split[0]}>");
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
            List<string> xmlContent = new List<string>
            {
                xmlHeader
            };

            string translatedName = GetTranslatedName(language);
            bool nowhitespace = TextManager.IsCJK(translatedName);

            int languageColumn = -1;
            string[] headerSplit = csvContent[0].Split(separator);
            for (int i = 0; i < headerSplit.Length; i++)
            {
                if (headerSplit[i] == language || 
                    (language == "English" && headerSplit[i]== "Line (Original)"))
                {
                    languageColumn = i;
                    break;
                }
            }

            xmlContent.Add($"<Conversations identifier=\"vanillaconversations\" Language=\"{language}\" nowhitespace=\"{nowhitespace}\">");

            conversationClosingIndent.Clear();
            int conversationStart = 1;

            xmlContent.Add(string.Empty);

            for (int i = conversationStart; i < csvContent.Length; i++) // Conversations
            {
                string[] split = csvContent[i].Split(separator);
                int emptyFields = 0;
                for (int j = 0; j < split.Length; j++)
                {
                    if (split[j] == string.Empty) { emptyFields++; }
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

                string line = split[languageColumn].Replace("\"", "");
                string speaker = split[2];
                int depthIndex = int.Parse(split[3]);
                // 3 = original line
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

                    if (nextConversationElement[3] != string.Empty)
                    {
                        nextDepth = int.Parse(nextConversationElement[3]);
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
                    //end of file, close remaining xml tags
                    xmlContent.Add(element.TrimEnd() + "/>");
                    for (int j = depthIndex - 1; j >= 0; j--)
                    {
                        HandleClosingElements(xmlContent, j);
                    }
                }
            }

            xmlContent.Add(string.Empty);
            xmlContent.Add("</Conversations>");

            return xmlContent;
        }

        private static void HandleClosingElements(List<string> xmlContent, int targetDepth)
        {
            if (conversationClosingIndent.Count == 0) { return; }

            for (int k = conversationClosingIndent.Count - 1; k >= 0; k--)
            {
                int currentIndent = conversationClosingIndent[k];
                if (currentIndent < targetDepth) { break; }
                xmlContent.Add($"{GetIndenting(currentIndent)}</Conversation>");
                conversationClosingIndent.RemoveAt(k);
            }
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
