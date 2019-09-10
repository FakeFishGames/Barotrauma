using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    public static class TextManager
    {
        //only used if none of the selected content packages contain any text files
        const string VanillaTextFilePath = "Content/Texts/EnglishVanilla.xml";

        //key = language
        private static Dictionary<string, List<TextPack>> textPacks = new Dictionary<string, List<TextPack>>();

        private static readonly string[] serverMessageCharacters = new string[] { "~", "[", "]", "=" };

        public static string Language;

        public static bool Initialized
        {
            get;
            private set;
        }

        private static readonly HashSet<string> availableLanguages = new HashSet<string>();
        public static IEnumerable<string> AvailableLanguages
        {
            get { return availableLanguages; }
        }

        public static List<string> GetTextFiles()
        {
            var list = new List<string>();
            GetTextFilesRecursive(Path.Combine("Content", "Texts"), ref list);
            return list;
        }

        private static void GetTextFilesRecursive(string directory, ref List<string> list)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                list.Add(file);
            }
            foreach (string subDir in Directory.GetDirectories(directory))
            {
                GetTextFilesRecursive(subDir, ref list);
            }
        }

        /// <summary>
        /// Returns the name of the language in the respective language
        /// </summary>
        public static string GetTranslatedLanguageName(string language)
        {
            if (!textPacks.ContainsKey(language))
            {
                return language;
            }

            foreach (var textPack in textPacks[language])
            {
                if (textPack.Language == language)
                {
                    return textPack.TranslatedName;
                }
            }
            return language;
        }

        public static void LoadTextPacks(IEnumerable<ContentPackage> selectedContentPackages)
        {
            availableLanguages.Clear();
            textPacks.Clear();
            var textFiles = ContentPackage.GetFilesOfType(selectedContentPackages, ContentType.Text);

            foreach (string file in textFiles)
            {
                try
                {
                    var textPack = new TextPack(file);
                    availableLanguages.Add(textPack.Language);
                    if (!textPacks.ContainsKey(textPack.Language))
                    {
                        textPacks.Add(textPack.Language, new List<TextPack>());
                    }
                    textPacks[textPack.Language].Add(textPack);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to load text file \"" + file + "\"!", e);
                }
            }

            if (textPacks.Count == 0)
            {
                DebugConsole.ThrowError("No text files available in any of the selected content packages. Attempting to find a vanilla English text file...");
                if (!File.Exists(VanillaTextFilePath))
                {
                    throw new Exception("No text files found in any of the selected content packages or in the default text path!");
                }
                var textPack = new TextPack(VanillaTextFilePath);
                availableLanguages.Add(textPack.Language);
                textPacks.Add(textPack.Language, new List<TextPack>() { textPack });
            }
            Initialized = true;
        }

        public static bool ContainsTag(string textTag)
        {
            if (string.IsNullOrEmpty(textTag)) { return false; }

            if (!textPacks.ContainsKey(Language))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + Language + ")! Switching to English...");
                Language = "English";
                if (!textPacks.ContainsKey(Language))
                {
                    throw new Exception("No text packs available in English!");
                }
            }
            foreach (TextPack textPack in textPacks[Language])
            {
                if (textPack.Get(textTag) != null) { return true; }
            }
            return false;
        }

        public static string Get(string textTag, bool returnNull = false, string fallBackTag = null)
        {
            if (!textPacks.ContainsKey(Language))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + Language + ")! Switching to English...");
                Language = "English";
                if (!textPacks.ContainsKey(Language))
                {
                    throw new Exception("No text packs available in English!");
                }
            }

            foreach (TextPack textPack in textPacks[Language])
            {
                string text = textPack.Get(textTag);
                if (text != null) { return text; }
            }

            if (!string.IsNullOrEmpty(fallBackTag))
            {
                foreach (TextPack textPack in textPacks[Language])
                {
                    string text = textPack.Get(fallBackTag);
                    if (text != null) { return text; }
                }
            }

            //if text was not found and we're using a language other than English, see if we can find an English version
            //may happen, for example, if a user has selected another language and using mods that haven't been translated to that language
            if (Language != "English" && textPacks.ContainsKey("English"))
            {
                foreach (TextPack textPack in textPacks["English"])
                {
                    string text = textPack.Get(textTag);
                    if (text != null)
                    {
#if DEBUG
                        DebugConsole.NewMessage("Text \"" + textTag + "\" not found for the language \"" + Language + "\". Using the English text \"" + text + "\" instead.");
#endif
                        return text;
                    }
                }
            }

            if (returnNull)
            {
                return null;
            }
            else
            {
                DebugConsole.ThrowError("Text \"" + textTag + "\" not found.");
                return textTag;
            }
        }

        public static string GetWithVariables(string textTag, string[] variableTags, string[] variableValues, bool[] formatCapitals = null, bool returnNull = false, string fallBackTag = null)
        {
            string text = Get(textTag, returnNull, fallBackTag);

            if (text == null || text.Length == 0 || variableTags.Length != variableValues.Length)
            {
#if DEBUG
                if (variableTags.Length != variableValues.Length)
                {
                    DebugConsole.ThrowError("variableTags.Length and variableValues.Length do not match for \"" + textTag + "\".");
                }

                if (formatCapitals != null && formatCapitals.Length != variableTags.Length)
                {
                    DebugConsole.ThrowError("variableTags.Length and formatCapitals.Length do not match for \"" + textTag + "\".");
                }
#endif
                if (returnNull)
                {
                    return null;
                }
                else
                {
                    return textTag;
                }
            }

            if (formatCapitals != null && !GameMain.Config.Language.Contains("Chinese"))
            {
                for (int i = 0; i < variableTags.Length; i++)
                {
                    if (formatCapitals[i])
                    {
                        variableValues[i] = HandleVariableCapitalization(text, variableTags[i], variableValues[i]);
                    }
                }
            }

            for (int i = 0; i < variableTags.Length; i++)
            {
                text = text.Replace(variableTags[i], variableValues[i]);
            }

            return text;
        }

        public static string GetWithVariable(string textTag, string variableTag, string variableValue, bool formatCapitals = false, bool returnNull = false, string fallBackTag = null)
        {
            string text = Get(textTag, returnNull, fallBackTag);

            if (text == null || text.Length == 0)
            {
                if (returnNull)
                {
                    return null;
                }
                else
                {
                    return textTag;
                }
            }

            if (formatCapitals && !GameMain.Config.Language.Contains("Chinese"))
            {
                variableValue = HandleVariableCapitalization(text, variableTag, variableValue);
            }

            return text.Replace(variableTag, variableValue);
        }

        private static string HandleVariableCapitalization(string text, string variableTag, string variableValue)
        {
            int index = text.IndexOf(variableTag) - 1;
            if (index == -1)
            {
                return variableValue;
            }

            for (int i = index; i >= 0; i--)
            {
                if (text[i] == ' ')
                {
                    continue;
                }
                else
                {
                    if (text[i] != '.')
                    {
                        variableValue = variableValue.ToLower();
                    }
                    else
                    {
                        variableValue = Capitalize(variableValue);
                        break;
                    }
                }
            }

            return variableValue;
        }

        public static string ParseInputTypes(string text)
        {
            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                text = text.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameMain.Config.KeyBind(inputType).ToString());
                text = text.Replace("[InputType." + inputType.ToString() + "]", GameMain.Config.KeyBind(inputType).ToString());
            }
            return text;
        }

        public static string GetFormatted(string textTag, bool returnNull = false, params object[] args)
        {
            string text = Get(textTag, returnNull);

            if (text == null || text.Length == 0)
            {
                if (returnNull)
                {
                    return null;
                }
                else
                {
                    DebugConsole.ThrowError("Text \"" + textTag + "\" not found.");
                    return textTag;
                }
            }

            try
            {
                return string.Format(text, args);  
            }   
            catch (FormatException)
            {
                string errorMsg = "Failed to format text \"" + text + "\", args: " + string.Join(", ", args);
                GameAnalyticsManager.AddErrorEventOnce("TextManager.GetFormatted:FormatException", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return text;
            }
        }

        public static string FormatServerMessage(string textId)
        {
            return $"{textId}~";
        }

        public static string FormatServerMessage(string message, IEnumerable<string> keys, IEnumerable<string> values)
        {
            if (keys == null || values == null || !keys.Any() || !values.Any())
            {
                return FormatServerMessage(message);
            }
            var startIndex = message.LastIndexOf('/') + 1;
            var endIndex = message.IndexOf('~', startIndex);
            if (endIndex == -1)
            {
                endIndex = message.Length - 1;
            }
            var textId = message.Substring(startIndex, endIndex - startIndex + 1);
            var keysWithValues = keys.Zip(values, (key, value) => new { Key = key, Value = value });
            var prefixEntries = keysWithValues.Select((kv, index) =>
            {
                if (kv.Value.IndexOfAny(new char[] { '~', '/' }) != -1)
                {
                    var kvStartIndex = kv.Value.LastIndexOf('/') + 1;
                    return kv.Value.Substring(0, kvStartIndex) + $"[{textId}.{index}]={kv.Value.Substring(kvStartIndex)}";
                }
                else
                {
                    return null;
                }
            }).Where(e => e != null).ToArray();
            return string.Join("",
                (prefixEntries.Length > 0 ? string.Join("/", prefixEntries) + "/" : ""),
                message,
                string.Join("", keysWithValues.Select((kv, index) => kv.Value.IndexOfAny(new char[] { '~', '/' }) != -1 ? $"~{kv.Key}=[{textId}.{index}]" : $"~{kv.Key}={kv.Value}").ToArray())
            );
        }

        static readonly string[] genderPronounVariables = new string[] {
            "[genderpronoun]",
            "[genderpronounpossessive]",
            "[genderpronounreflexive]",
            "[Genderpronoun]",
            "[Genderpronounpossessive]",
            "[Genderpronounreflexive]"
        };

        static readonly string[] genderPronounMaleValues = new string[] {
             "PronounMaleLowercase",
             "PronounPossessiveMaleLowercase",
             "PronounReflexiveMaleLowercase",
             "PronounMale",
             "PronounPossessiveMale",
             "PronounReflexiveMale"
        };

        static readonly string[] genderPronounFemaleValues = new string[] {
             "PronounFemaleLowercase",
             "PronounPossessiveFemaleLowercase",
             "PronounReflexiveFemaleLowercase",
             "PronounMale",
             "PronounPossessiveFemale",
             "PronounReflexiveFemale"
        };

        public static string FormatServerMessageWithGenderPronouns(Gender gender, string message, IEnumerable<string> keys, IEnumerable<string> values)
        {
            return FormatServerMessage(message, keys.Concat(genderPronounVariables), values.Concat(gender == Gender.Male ? genderPronounMaleValues : genderPronounFemaleValues));
        }

        static readonly Regex reReplacedMessage = new Regex(@"^(?<variable>[\[\].A-Za-z0-9_]+?)=(?<message>.*)$", RegexOptions.Compiled);

        // Format: ServerMessage.Identifier1/ServerMessage.Indentifier2~[variable1]=value~[variable2]=value
        // Also: replacement=ServerMessage.Identifier1~[variable1]=value/ServerMessage.Identifier2~[variable2]=replacement
        public static string GetServerMessage(string serverMessage)
        {
            if (!textPacks.ContainsKey(Language))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + Language + ")! Switching to English...");
                Language = "English";
                if (!textPacks.ContainsKey(Language))
                {
                    throw new Exception("No text packs available in English!");
                }
            }

            string[] messages = serverMessage.Split('/');
            var replacedMessages = new Dictionary<string, string>();

            bool translationsFound = false;

            try
            {
                for (int i = 0; i < messages.Length; i++)
                {
                    if (messages[i].EndsWith("~", StringComparison.Ordinal))
                    {
                        messages[i] = messages[i].Substring(0, messages[i].Length - 1);
                    }
                    if (!IsServerMessageWithVariables(messages[i]) && !messages[i].Contains('=')) // No variables, try to translate
                    {
                        foreach (var replacedMessage in replacedMessages)
                        {
                            messages[i] = messages[i].Replace(replacedMessage.Key, replacedMessage.Value);
                        }

                        if (messages[i].Contains(" ")) continue; // Spaces found, do not translate
                        string msg = Get(messages[i], true);
                        if (msg != null) // If a translation was found, otherwise use the original
                        {
                            messages[i] = msg;
                            translationsFound = true;
                        }
                    }
                    else
                    {
                        var match = reReplacedMessage.Match(messages[i]);
                        string messageVariable = null;
                        if (match.Success)
                        {
                            messageVariable = match.Groups["variable"].ToString();
                            messages[i] = match.Groups["message"].ToString();
                        }

                        foreach (var replacedMessage in replacedMessages)
                        {
                            messages[i] = messages[i].Replace(replacedMessage.Key, replacedMessage.Value);
                        }


                        string[] messageWithVariables = messages[i].Split('~');

                        string msg = Get(messageWithVariables[0], true);

                        if (msg != null) // If a translation was found, otherwise use the original
                        {
                            messages[i] = msg;
                            translationsFound = true;
                        }
                        else if (messageVariable == null)
                        {
                            continue; // No translation found, probably caused by player input -> skip variable handling
                        }

                        // First index is always the message identifier -> start at 1
                        for (int j = 1; j < messageWithVariables.Length; j++)
                        {
                            string[] variableAndValue = messageWithVariables[j].Split('=');
                            messages[i] = messages[i].Replace(variableAndValue[0], variableAndValue[1]);
                        }

                        if (messageVariable != null)
                        {
                            replacedMessages[messageVariable] = messages[i];
                            messages[i] = null;
                        }
                    }
                }

                if (translationsFound)
                {
                    string translatedServerMessage = string.Empty;
                    for (int i = 0; i < messages.Length; i++)
                    {
                        if (messages[i] != null)
                        {
                            translatedServerMessage += messages[i];
                        }
                    }
                    return translatedServerMessage;
                }
                else
                {
                    return serverMessage;
                }
            }

            catch (IndexOutOfRangeException exception)
            {
                string errorMsg = "Failed to translate server message \"" + serverMessage + "\".";
#if DEBUG
                DebugConsole.ThrowError(errorMsg, exception);
#endif
                GameAnalyticsManager.AddErrorEventOnce("TextManager.GetServerMessage:" + serverMessage, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return errorMsg;
            }
        }

        public static bool IsServerMessageWithVariables(string message)
        {
            for (int i = 0; i < serverMessageCharacters.Length; i++)
            {
                if (!message.Contains(serverMessageCharacters[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Adds a punctuation symbol between two strings, taking into account special rules in some locales (e.g. non-breaking space before a colon in French)
        /// </summary>
        public static string AddPunctuation(char punctuationSymbol, params string[] texts)
        {
            string separator = "";
            switch (GameMain.Config.Language)
            {
                case "French":
                    bool addNonBreakingSpace =
                        punctuationSymbol == ':' || punctuationSymbol == ';' ||
                        punctuationSymbol == '!' || punctuationSymbol == '?';
                    separator = addNonBreakingSpace ?
                        new string(new char[] { (char)(0xA0), punctuationSymbol, ' ' }) :
                        new string(new char[] { punctuationSymbol, ' ' });
                    break;
                default:
                    separator = new string(new char[] { punctuationSymbol, ' ' });
                    break;
            }
            return string.Join(separator, texts);
        }

        public static string EnsureUTF8(string text)
        {
            byte[] bytes = Encoding.Default.GetBytes(text);
            return Encoding.UTF8.GetString(bytes);
        }

        public static List<string> GetAll(string textTag)
        {
            if (!textPacks.ContainsKey(Language))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + Language + ")! Switching to English...");
                Language = "English";
                if (!textPacks.ContainsKey(Language))
                {
                    throw new Exception("No text packs available in English!");
                }
            }

            List<string> allText;

            foreach (TextPack textPack in textPacks[Language])
            {
                allText = textPack.GetAll(textTag);
                if (allText != null) return allText;
            }

            //if text was not found and we're using a language other than English, see if we can find an English version
            //may happen, for example, if a user has selected another language and using mods that haven't been translated to that language
            if (Language != "English" && textPacks.ContainsKey("English"))
            {
                foreach (TextPack textPack in textPacks["English"])
                {
                    allText = textPack.GetAll(textTag);
                    if (allText != null) return allText;
                }
            }

            return null;
        }

        public static List<KeyValuePair<string, string>> GetAllTagTextPairs()
        {
            if (!textPacks.ContainsKey(Language))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + Language + ")! Switching to English...");
                Language = "English";
                if (!textPacks.ContainsKey(Language))
                {
                    throw new Exception("No text packs available in English!");
                }
            }

            List<KeyValuePair<string, string>> allText = new List<KeyValuePair<string, string>>();

            foreach (TextPack textPack in textPacks[Language])
            {
                allText.AddRange(textPack.GetAllTagTextPairs());
            }

            return allText;
        }

        public static string ReplaceGenderPronouns(string text, Gender gender)
        {
            if (gender == Gender.Male)
            {
                return text.Replace("[genderpronoun]",     Get("PronounMaleLowercase"))
                    .Replace("[genderpronounpossessive]",  Get("PronounPossessiveMaleLowercase"))
                    .Replace("[genderpronounreflexive]",   Get("PronounReflexiveMaleLowercase"))
                    .Replace("[Genderpronoun]",            Get("PronounMale"))
                    .Replace("[Genderpronounpossessive]",  Get("PronounPossessiveMale"))
                    .Replace("[Genderpronounreflexive]",   Get("PronounReflexiveMale"));
            }
            else
            {
                return text.Replace("[genderpronoun]",     Get("PronounFemaleLowercase"))
                    .Replace("[genderpronounpossessive]",  Get("PronounPossessiveFemaleLowercase"))
                    .Replace("[genderpronounreflexive]",   Get("PronounReflexiveFemaleLowerCase"))
                    .Replace("[Genderpronoun]",            Get("PronounFemale"))
                    .Replace("[Genderpronounpossessive]",  Get("PronounPossessiveFemale"))
                    .Replace("[Genderpronounreflexive]",   Get("PronounReflexiveFemale"));
            }
        }

        static Regex isCJK = new Regex(
            @"\p{IsHangulJamo}|" +
            @"\p{IsHiragana}|" +
            @"\p{IsKatakana}|" +
            @"\p{IsCJKRadicalsSupplement}|" +
            @"\p{IsCJKSymbolsandPunctuation}|" +
            @"\p{IsEnclosedCJKLettersandMonths}|" +
            @"\p{IsCJKCompatibility}|" +
            @"\p{IsCJKUnifiedIdeographsExtensionA}|" +
            @"\p{IsCJKUnifiedIdeographs}|" +
            @"\p{IsHangulSyllables}|" +
            @"\p{IsCJKCompatibilityForms}");

        /// <summary>
        /// Does the string contain symbols from Chinese, Japanese or Korean languages
        /// </summary>
        public static bool IsCJK(string text)
        {
            if (string.IsNullOrEmpty(text)) { return false; }
            return isCJK.IsMatch(text);
        }

#if DEBUG
        public static void CheckForDuplicates(string lang)
        {
            if (!textPacks.ContainsKey(lang))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + lang + ")!");
                return;
            }

            int packIndex = 0;
            foreach (TextPack textPack in textPacks[lang])
            {
                textPack.CheckForDuplicates(packIndex);
                packIndex++;
            }
        }

        public static void WriteToCSV()
        {
            string lang = "English";

            if (!textPacks.ContainsKey(lang))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + lang + ")!");
                return;
            }

            int packIndex = 0;
            foreach (TextPack textPack in textPacks[lang])
            {
                textPack.WriteToCSV(packIndex);
                packIndex++;
            }
        }
#endif

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
