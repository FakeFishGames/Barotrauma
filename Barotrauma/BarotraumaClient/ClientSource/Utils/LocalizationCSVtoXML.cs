#if DEBUG
using Barotrauma.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    class LocalizationCSVtoXML
    {
        private static readonly List<int> conversationClosingIndent = new List<int>();
        private static readonly char[] separator = new char[1] { '|' };

        /// <summary>
        /// Anything between square brackets is assumed to be a tag (e.g. [location], [amount]) that must be present in the translations as-is.
        /// Bracketed text with whitespace (e.g. [Connection lost.]) is assumed to be just text, not a tag.
        /// </summary>
        private static readonly Regex tagRegex = new Regex(@"\[[^\[\]\s]+\]", RegexOptions.Compiled);

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
            for (int i = 0; i < 6; i++)
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
                    case 2:
                        textFilePath = Path.Combine(infoTextPath, "EndGameTexts.csv");
                        outputFileName = "VanillaEndGameTexts.xml";
                        break;
                    case 3:
                        textFilePath = Path.Combine(infoTextPath, "FactionTexts.csv");
                        outputFileName = "VanillaFactions.xml";
                        break;
                    case 4:
                        textFilePath = Path.Combine(infoTextPath, "DLC.csv");
                        outputFileName = "DLCFinal.xml";
                        break;
                    case 5:
                        textFilePath = Path.Combine(infoTextPath, "DLCCompanionUpdate.csv");
                        outputFileName = "DLCCompanionUpdateFinal.xml";
                        break;
                    default:
                        throw new NotImplementedException();
                }

                Dictionary<string, List<string>> xmlContent;
                try
                {
                    xmlContent = ConvertInfoTextToXML(File.ReadAllLines(textFilePath, Encoding.UTF8, catchUnauthorizedAccessExceptions: false), textFilePath);
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
                TryConvertConversationCSV("DLCConversations.csv", languages, outputConversationsDirectory);
                foreach (Identifier identifier in NPCConversationCollection.Collections[GameSettings.CurrentConfig.Language].Keys)
                {
                    var collection = NPCConversationCollection.Collections[GameSettings.CurrentConfig.Language][identifier];
                    TryConvertConversationCSV($"DLCConversations_{collection.Identifier}.csv", languages, outputConversationsDirectory);                    
                }

                string conversationFilePath = Path.Combine(infoTextPath, "DLCConversations.csv");
                var conversationLinesAll = File.ReadAllLines(conversationFilePath, Encoding.UTF8);
                foreach (string language in languages)
                {
                    List<string> convXmlContent = ConvertConversationsToXML(conversationLinesAll, language, conversationFilePath);
                    if (convXmlContent == null)
                    {
                        DebugConsole.ThrowError("NPCConversation Localization .csv to .xml conversion failed for: " + language);
                        continue;
                    }
                    string languageNoWhitespace = language.Replace(" ", "");
                    string xmlFileFullPath = Path.Combine(outputConversationsDirectory, languageNoWhitespace, $"DLCConversations_{languageNoWhitespace}.xml");
                    File.WriteAllLines(xmlFileFullPath, convXmlContent, Encoding.UTF8);
                    DebugConsole.NewMessage("Conversation localization .xml file successfully created at: " + xmlFileFullPath);
                }
            }
        }

        private static bool TryConvertConversationCSV(string csvPath, List<string> languages, string outputConversationsDirectory)
        {
            string conversationFilePath = Path.Combine(infoTextPath, csvPath);
            if (!File.Exists(conversationFilePath))
            {
                DebugConsole.AddWarning($"No NPC conversation file found in {conversationFilePath}, skipping...");
                return false; 
            }
            var conversationLinesAll = File.ReadAllLines(conversationFilePath, Encoding.UTF8);
            foreach (string language in languages)
            {
                List<string> convXmlContent = ConvertConversationsToXML(conversationLinesAll, language, conversationFilePath);
                if (convXmlContent == null)
                {
                    DebugConsole.ThrowError("NPCConversation Localization .csv to .xml conversion failed for: " + language);
                    continue;
                }
                string languageNoWhitespace = language.Replace(" ", "");
                string xmlFileFullPath = Path.Combine(outputConversationsDirectory, languageNoWhitespace, $"{Path.GetFileNameWithoutExtension(csvPath)}_{languageNoWhitespace}.xml");
                File.WriteAllLines(xmlFileFullPath, convXmlContent, Encoding.UTF8);
                DebugConsole.NewMessage($"CSV conversation from {csvPath} converted to XML file in {xmlFileFullPath}.");
            }
            return true;
        }

        private static Dictionary<string, List<string>> ConvertInfoTextToXML(string[] csvContent, string filePath)
        {
            Dictionary<string, List<string>> xmlContentByLanguage = new Dictionary<string, List<string>>();

            //get all the languages from the header row
            string headerRow = csvContent[0];
            var headerContent = headerRow.Split(separator);
            int englishColumn = Array.IndexOf(headerContent, "English");
            int commentsColumn = Array.FindIndex(headerContent, h => h.Equals("comments", StringComparison.OrdinalIgnoreCase));
            List<int> languageColumns = new List<int>();
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
                languageColumns.Add(i);
            }

            foreach ((int row, string[] split) in SplitCsvRows(csvContent, headerContent, commentsColumn, filePath))
            {
                //all values empty = an empty line
                if (split.All(s => s.IsNullOrEmpty()))
                {
                    AddToAllLanguages(string.Empty);
                    continue;
                }

                string tag = split[0];

                //value is empty in all languages (the comments column doesn't matter here)
                if (languageColumns.All(j => split[j].IsNullOrEmpty()))
                {
                    //nothing but a comment in the comments column, nothing to export
                    if (tag.IsNullOrEmpty()) { continue; }
                    //first line is all lower-case and contains dot, assume it's an empty value
                    if (tag.Contains(".") && !tag.Any(char.IsUpper))
                    {
                        AddToAllLanguages($"<{tag}></{tag}>");
                    }
                    //otherwise assume it's a comment
                    else
                    {
                        AddToAllLanguages($"<!-- {tag} -->");
                    }
                    continue;
                }

                //texts without a valid tag can't be referenced, and would produce invalid xml (e.g. <>text</>)
                //happens e.g. if the tag and English text have been removed from the sheet, but the old translations have been left in
                if (!IsValidTag(tag))
                {
                    DebugConsole.AddWarning(
                        $"Invalid tag \"{tag}\" on line {row} in {filePath}, skipping the row. " +
                        $"The tag must be a valid xml element name (not empty, no whitespace or special characters). " +
                        $"English: {(englishColumn > -1 ? split[englishColumn] : string.Empty)}");
                    continue;
                }

                foreach (int j in languageColumns)
                {
                    string languageName = headerContent[j];
                    string textContent = split[j];
                    if (textContent == "#NAME?")
                    {
                        throw new Exception(
                            $"Error while converting csv to xml: #NAME? value found on line {row}, language: {languageName}." +
                            " This indicates a missing value in the csv file (some text got accidentally converted to a broken formula in the localization sheet?).");
                    }
                    if (englishColumn > -1 && j != englishColumn)
                    {
                        WarnAboutMismatchedTags(split[englishColumn], textContent, languageName, $"\"{tag}\" (line {row} in {filePath})");
                    }
                    textContent = textContent.Replace(" & ", " &amp; ");
                    xmlContentByLanguage[languageName].Add($"<{tag}>{textContent}</{tag}>");
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

        /// <summary>
        /// Splits the csv lines (excluding the header) into rows of values.
        /// A value with newlines (typically a multi-line comment) is split on multiple lines in the csv,
        /// those lines are merged back into one row based on the number of columns in the header.
        /// </summary>
        /// <returns>The values of each row, along with the line number the row starts on.</returns>
        private static List<(int row, string[] values)> SplitCsvRows(string[] csvContent, string[] headerContent, int commentsColumn, string filePath)
        {
            var rows = new List<(int row, string[] values)>();
            string pendingLine = null;
            int pendingLineNumber = 0;
            for (int i = 1; i < csvContent.Length; i++) // Start at one to ignore header
            {
                if (pendingLine == null)
                {
                    //empty line
                    if (csvContent[i].Length == 0)
                    {
                        rows.Add((i, Enumerable.Repeat(string.Empty, headerContent.Length).ToArray()));
                        continue;
                    }
                    pendingLine = csvContent[i];
                    pendingLineNumber = i;
                }
                else
                {
                    pendingLine += "\n" + csvContent[i];
                }

                string[] values = pendingLine.Split(separator);
                if (values.Length < headerContent.Length)
                {
                    //not enough values yet, assume a value continues on the next line
                    continue;
                }
                if (values.Length > headerContent.Length)
                {
                    throw new Exception(
                        $"Error while converting csv to xml: too many values in the row starting on line {pendingLineNumber} ({pendingLine}). " +
                        "Check that there's no extra separators or missing values in the csv file.");
                }
                for (int j = 0; j < values.Length; j++)
                {
                    if (j != commentsColumn && values[j].Contains('\n'))
                    {
                        DebugConsole.AddWarning(
                            $"Newline in column \"{headerContent[j]}\" in the row starting on line {pendingLineNumber} in {filePath}. " +
                            "Newlines are only expected in the comments column, check that the row was parsed correctly.");
                    }
                }
                rows.Add((pendingLineNumber, values));
                pendingLine = null;
            }
            if (pendingLine != null)
            {
                throw new Exception(
                    $"Error while converting csv to xml: not enough values in the row starting on line {pendingLineNumber} ({pendingLine}). " +
                    "Check that there's no missing separators or values in the csv file.");
            }
            return rows;
        }

        private static bool IsValidTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) { return false; }
            try
            {
                System.Xml.XmlConvert.VerifyName(tag);
                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
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

        private static int GetConversationLanguageColumn(string[] headerSplit, string language)
        {
            for (int i = 0; i < headerSplit.Length; i++)
            {
                if (headerSplit[i] == language ||
                    (language == "English" && headerSplit[i] == "Line (Original)"))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Warns if the translated text is missing any of the tags (anything between square brackets) present in the english text.
        /// The tags must be present in the exact same form, because they're replaced with variables in the code.
        /// </summary>
        private static void WarnAboutMismatchedTags(string englishText, string translatedText, string language, string debugLocation)
        {
            //missing translations are a separate issue, no need to warn about tags in them
            if (string.IsNullOrEmpty(englishText) || string.IsNullOrEmpty(translatedText)) { return; }

            var missingTags = tagRegex.Matches(englishText)
                .Select(m => m.Value)
                .Distinct()
                .Where(tag => !translatedText.Contains(tag))
                .ToList();
            if (!missingTags.Any()) { return; }

            DebugConsole.AddWarning(
                $"Mismatched tags in {debugLocation}, language: {language}. Missing tags: {string.Join(", ", missingTags)}.\n" +
                $"English: {englishText}\n" +
                $"{language}: {translatedText}");
        }

        private static List<string> ConvertConversationsToXML(string[] csvContent, string language, string filePath)
        {
            List<string> xmlContent = new List<string>
            {
                xmlHeader
            };

            string translatedName = GetTranslatedName(language);
            bool nowhitespace = TextManager.IsCJK(translatedName);

            string[] headerSplit = csvContent[0].Split(separator);
            int languageColumn = GetConversationLanguageColumn(headerSplit, language);
            int englishColumn = GetConversationLanguageColumn(headerSplit, "English");

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
                if (englishColumn > -1 && languageColumn != englishColumn)
                {
                    WarnAboutMismatchedTags(split[englishColumn].Replace("\"", ""), line, language, $"line {i} in {filePath}");
                }
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
