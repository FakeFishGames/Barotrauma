using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Barotrauma.Extensions;
using System.Xml.Linq;

namespace Barotrauma
{
    public static class TextManager
    {
        //only used if none of the selected content packages contain any text files
        const string VanillaTextFilePath = "Content/Texts/English/EnglishVanilla.xml";

        private static readonly object mutex = new object();

        //key = language
        private static Dictionary<string, List<TextPack>> textPacks;

        private static readonly string[] serverMessageCharacters = new string[] { "~", "[", "]", "=" };

        public static string Language;

        public static bool Initialized
        {
            get;
            private set;
        }

        private static HashSet<string> availableLanguages;
        public static IEnumerable<string> AvailableLanguages
        {
            get
            {
                lock (mutex)
                {
                    return new HashSet<string>(availableLanguages);
                }
            }
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
            lock (mutex)
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
        }

        public static void LoadTextPacks(IEnumerable<ContentPackage> selectedContentPackages)
        {
            HashSet<string> newLanguages = new HashSet<string>();
            Dictionary<string, List<TextPack>> newTextPacks = new Dictionary<string, List<TextPack>>();

            var textFiles = ContentPackage.GetFilesOfType(selectedContentPackages, ContentType.Text).ToList();

            foreach (ContentFile file in textFiles)
            {
#if !DEBUG
                try
                {
#endif
                    var textPack = new TextPack(file.Path);
                    newLanguages.Add(textPack.Language);
                    if (!newTextPacks.ContainsKey(textPack.Language))
                    {
                        newTextPacks.Add(textPack.Language, new List<TextPack>());
                    }
                    newTextPacks[textPack.Language].Add(textPack);
#if !DEBUG
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to load text file \"" + file.Path + "\"!", e);
                }
#endif
            }

            if (newTextPacks.Count == 0)
            {
                DebugConsole.ThrowError("No text files available in any of the selected content packages. Attempting to find a vanilla English text file...");
                if (!File.Exists(VanillaTextFilePath))
                {
                    throw new Exception("No text files found in any of the selected content packages or in the default text path!");
                }
                var textPack = new TextPack(VanillaTextFilePath);
                newLanguages.Add(textPack.Language);
                newTextPacks.Add(textPack.Language, new List<TextPack>() { textPack });
            }

            if (newTextPacks.Count == 0)
            {
                throw new Exception("Failed to load text packs!");
            }

            lock (mutex)
            {
                textPacks = newTextPacks;
                availableLanguages = newLanguages;
                DebugConsole.NewMessage("Loaded languages: " + string.Join(", ", newLanguages));
            }

            Initialized = true;
        }

        public static void LoadTextPack(string file)
        {
            lock (mutex)
            {
                var textPack = new TextPack(file);
                availableLanguages.Add(textPack.Language);
                if (!textPacks.ContainsKey(textPack.Language))
                {
                    textPacks.Add(textPack.Language, new List<TextPack>());
                }
                textPacks[textPack.Language].Add(textPack);
            }
        }

        public static void RemoveTextPack(string file)
        {
            List<string> keysToRemove = new List<string>();
            foreach (var textPackKVP in textPacks)
            {
                var textPackLanguage = textPackKVP.Key;
                var textPackList = textPackKVP.Value;
                for (int i = 0; i < textPackList.Count; i++)
                {
                    if (textPackList[i].FilePath == file)
                    {
                        textPackList.Remove(textPackList[i]);
                        if (textPackList.Count == 0)
                        {
                            keysToRemove.Add(textPackLanguage);
                        }
                    }
                }
            }

            foreach (var key in keysToRemove)
            {
                availableLanguages.Remove(key);
                textPacks.Remove(key);
            }
        }

        public static bool ContainsTag(string textTag)
        {
            if (string.IsNullOrEmpty(textTag)) { return false; }

            lock (mutex)
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
                    if (textPack.Get(textTag) != null) { return true; }
                }
            }

            return false;
        }

        private static readonly List<string> availableTexts = new List<string>();

        public static string Get(string textTag, bool returnNull = false, string fallBackTag = null, bool useEnglishAsFallBack = true)
        {
            lock (mutex)
            {
                if (textPacks == null)
                {
                    DebugConsole.ThrowError($"Failed to get the text \"{textTag}\" (no text packs loaded).");
                    return textTag;
                }

                if (!textPacks.ContainsKey(Language))
                {
                    DebugConsole.ThrowError("No text packs available for the selected language (" + Language + ")! Switching to English...");
                    Language = "English";
                    if (!textPacks.ContainsKey(Language))
                    {
                        throw new Exception("No text packs available in English!");
                    }
                }

#if DEBUG
                if (GameMain.Config != null && GameMain.Config.TextManagerDebugModeEnabled)
                {
                    return textTag;
                }
#endif
                availableTexts.Clear();
                foreach (TextPack textPack in textPacks[Language])
                {
                    var texts = textPack.GetAll(textTag);
                    if (texts != null)
                    {
                        availableTexts.AddRange(texts);
                    }
                }

                if (availableTexts.Any())
                {
                    return availableTexts.GetRandom().Replace(@"\n", "\n");
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
                if (useEnglishAsFallBack && Language != "English" && textPacks.ContainsKey("English"))
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

            if (formatCapitals != null && (GameMain.Config == null || !GameMain.Config.Language.Contains("Chinese")))
            {
                for (int i = 0; i < variableTags.Length; i++)
                {
                    if (string.IsNullOrEmpty(variableValues[i])) { continue; }
                    if (formatCapitals[i])
                    {
                        variableValues[i] = HandleVariableCapitalization(text, variableTags[i], variableValues[i]);
                    }
                }
            }

            for (int i = 0; i < variableTags.Length; i++)
            {
                if (variableValues[i] == null) 
                {
#if DEBUG
                    DebugConsole.ThrowError("Error in TextManager.GetWithVariables (variable " + i + " was null).\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    continue; 
                }
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

            if (variableValue == null)
            {
                variableValue = "null";
#if DEBUG
                throw new ArgumentException($"Variable value \"{variableTag}\" was null.");
#endif
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

        //TODO: the server should not be doing this!
        public static string ParseInputTypes(string text)
        {
#if CLIENT
            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                text = text.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameMain.Config.KeyBindText(inputType));
                text = text.Replace("[InputType." + inputType.ToString() + "]", GameMain.Config.KeyBindText(inputType));
            }
#endif
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

        /// <summary>
        /// Constructs a string from XML in a way that allows replacing one or more variables with hard-coded or localized values. Usage example in the method's comments.
        /// </summary>
        public static void ConstructDescription(ref string Description, XElement descriptionElement)
        {
            /*
            <Description tag="talentdescription.simultaneousskillgain">
                <Replace tag="[skillname1]" value="skillname.helm"/>
                <Replace tag="[skillname2]" value="skillname.weapons"/>
                <Replace tag="[somevalue]" value="45.3"/>
            </Description>
            */

            string extraDescriptionLine = Get(descriptionElement.GetAttributeString("tag", string.Empty));
            if (string.IsNullOrEmpty(extraDescriptionLine)) { return; }
            foreach (XElement replaceElement in descriptionElement.Elements())
            {
                if (replaceElement.Name.ToString().ToLowerInvariant() != "replace") { continue; }

                string tag = replaceElement.GetAttributeString("tag", string.Empty);
                string[] replacementValues = replaceElement.GetAttributeStringArray("value", new string[0]);
                string replacementValue = string.Empty;
                for (int i = 0; i < replacementValues.Length; i++)
                {
                    replacementValue += Get(replacementValues[i], returnNull: true) ?? replacementValues[i];
                    if (i < replacementValues.Length - 1)
                    {
                        replacementValue += ", ";
                    }
                }
                extraDescriptionLine = extraDescriptionLine.Replace(tag, replacementValue);
            }
            if (!string.IsNullOrEmpty(Description)) { Description += "\n"; }
            Description += extraDescriptionLine;
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

        static readonly string[] genderPronounVariables = {
            "[genderpronoun]",
            "[genderpronounpossessive]",
            "[genderpronounreflexive]",
            "[Genderpronoun]",
            "[Genderpronounpossessive]",
            "[Genderpronounreflexive]"
        };

        static readonly string[] genderPronounMaleValues = {
             "PronounMaleLowercase",
             "PronounPossessiveMaleLowercase",
             "PronounReflexiveMaleLowercase",
             "PronounMale",
             "PronounPossessiveMale",
             "PronounReflexiveMale"
        };

        static readonly string[] genderPronounFemaleValues = {
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

        // Same as string.Join(separator, parts) but performs the operation taking into account server message string replacements.
        public static string JoinServerMessages(string separator, string[] parts, string namePrefix = "part.")
        {

            return string.Join("/",
                string.Join("/", parts.Select((part, index) =>
                {
                    var partStart = part.LastIndexOf('/') + 1;
                    return partStart > 0 ? $"{part.Substring(0, partStart)}/[{namePrefix}{index}]={part.Substring(partStart)}" : $"[{namePrefix}{index}]={part.Substring(partStart)}";
                })),
                string.Join(separator, parts.Select((part, index) => $"[{namePrefix}{index}]")));
        }

        static readonly Regex reFormattedMessage = new Regex(@"^(?<variable>[\[\].a-z0-9_]+?)=(?<formatter>[a-z0-9_]+?)\((?<value>.+?)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex reReplacedMessage = new Regex(@"^(?<variable>[\[\].a-z0-9_]+?)=(?<message>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Dictionary<string, Func<string, string>> messageFormatters = new Dictionary<string, Func<string, string>>
        {
            { "duration", secondsValue => double.TryParse(secondsValue, out var seconds) ? $"{TimeSpan.FromSeconds(seconds):g}" : null }
        };

        // Format: ServerMessage.Identifier1/ServerMessage.Indentifier2~[variable1]=value~[variable2]=value
        // Also: replacement=ServerMessage.Identifier1~[variable1]=value/ServerMessage.Identifier2~[variable2]=replacement
        // And: replacement=formatter(value)
        // Variable that requires translation -> ServerMessage.Indentifier1~[variable1]=§value
        public static string GetServerMessage(string serverMessage)
        {
            lock (mutex)
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
                        string messageVariable = null;
                        var matchFormatted = reFormattedMessage.Match(messages[i]);
                        if (matchFormatted.Success)
                        {
                            var formatter = matchFormatted.Groups["formatter"].ToString();
                            if (messageFormatters.TryGetValue(formatter, out var formatterFn))
                            {
                                var formattedValue = formatterFn(matchFormatted.Groups["value"].ToString());
                                if (formattedValue != null)
                                {
                                    messageVariable = matchFormatted.Groups["variable"].ToString();
                                    messages[i] = formattedValue;
                                }
                            }
                        }
                        if (messageVariable == null)
                        {
                            var matchReplaced = reReplacedMessage.Match(messages[i]);
                            if (matchReplaced.Success)
                            {
                                messageVariable = matchReplaced.Groups["variable"].ToString();
                                messages[i] = matchReplaced.Groups["message"].ToString();
                            }
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
                            messages[i] = messages[i].Replace(variableAndValue[0], variableAndValue[1].Length > 1 && variableAndValue[1][0] == '§' ? Get(variableAndValue[1].Substring(1)) : variableAndValue[1]);
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

        /// <summary>
        /// Fetches a single variable from a servermessage
        /// </summary>
        public static string GetServerMessageVariable(string message, string variable)
        {
            int variableIndex = message.IndexOf(variable);
            if (variableIndex == -1)
            {
#if DEBUG
                DebugConsole.ThrowError($"Server message variable: '{variable}' not found in message: '{message}'");
#endif
                return string.Empty;
            }

            int startIndex = message.IndexOf('=', variableIndex) + 1;
            int endIndex = startIndex;

            for (int i = startIndex; i < message.Length; i++)
            {
                if (message[i] == '/' || message[i] == '~')
                {
                    endIndex = i;
                    break;
                }
            }

            if (endIndex == startIndex) endIndex = message.Length;

            return message.Substring(startIndex, endIndex - startIndex);
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

        public static List<string> GetAll(string textTag)
        {
            lock (mutex)
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
        }

        public static List<KeyValuePair<string, string>> GetAllTagTextPairs()
        {
            lock (mutex)
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
        }

        public static string ReplaceGenderPronouns(string text, Gender gender)
        {
            if (gender == Gender.Male)
            {
                return text.Replace("[genderpronoun]", Get("PronounMaleLowercase"))
                    .Replace("[genderpronounpossessive]", Get("PronounPossessiveMaleLowercase"))
                    .Replace("[genderpronounreflexive]", Get("PronounReflexiveMaleLowercase"))
                    .Replace("[Genderpronoun]", Get("PronounMale"))
                    .Replace("[Genderpronounpossessive]", Get("PronounPossessiveMale"))
                    .Replace("[Genderpronounreflexive]", Get("PronounReflexiveMale"));
            }
            else
            {
                return text.Replace("[genderpronoun]", Get("PronounFemaleLowercase"))
                    .Replace("[genderpronounpossessive]", Get("PronounPossessiveFemaleLowercase"))
                    .Replace("[genderpronounreflexive]", Get("PronounReflexiveFemaleLowerCase"))
                    .Replace("[Genderpronoun]", Get("PronounFemale"))
                    .Replace("[Genderpronounpossessive]", Get("PronounPossessiveFemale"))
                    .Replace("[Genderpronounreflexive]", Get("PronounReflexiveFemale"));
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
            lock (mutex)
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
        }

        public static void WriteToCSV()
        {
            lock (mutex)
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
        }
#endif

        public static string Capitalize(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return str;
            }

            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}
