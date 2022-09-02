#nullable enable

using Barotrauma.IO;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Globalization;

namespace Barotrauma
{
    public enum FormatCapitals
    {
        Yes, No
    }

    public static class TextManager
    {
        public readonly static LanguageIdentifier DefaultLanguage = "English".ToLanguageIdentifier();
        public readonly static ConcurrentDictionary<LanguageIdentifier, ImmutableHashSet<TextPack>> TextPacks = new ConcurrentDictionary<LanguageIdentifier, ImmutableHashSet<TextPack>>();
        public static IEnumerable<LanguageIdentifier> AvailableLanguages => TextPacks.Keys;

        private readonly static Dictionary<Identifier, WeakReference<TagLString>> cachedStrings =
            new Dictionary<Identifier, WeakReference<TagLString>>();
        private static ImmutableHashSet<Identifier> nonCacheableTags =
            ImmutableHashSet<Identifier>.Empty;

        public static int LanguageVersion { get; private set; } = 0;

        private readonly static Regex isCJK = new Regex(
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
        public static bool IsCJK(LocalizedString text)
        {
            return IsCJK(text.Value);
        }

        public static bool IsCJK(string text)
        {
            if (string.IsNullOrEmpty(text)) { return false; }
            return isCJK.IsMatch(text);
        }

        /// <summary>
        /// Check if the currently selected language is available, and switch to English if not
        /// </summary>
        public static void VerifyLanguageAvailable()
        {
            if (!TextPacks.ContainsKey(GameSettings.CurrentConfig.Language))
            {
                DebugConsole.ThrowError($"Could not find the language \"{GameSettings.CurrentConfig.Language}\". Trying to switch to English...");
                var config = GameSettings.CurrentConfig;
                config.Language = "English".ToLanguageIdentifier();;
                GameSettings.SetCurrentConfig(config);
            }
        }

        public static bool ContainsTag(string tag)
        {
            return ContainsTag(tag.ToIdentifier());
        }

        public static bool ContainsTag(Identifier tag)
        {
            return TextPacks[GameSettings.CurrentConfig.Language].Any(p => p.Texts.ContainsKey(tag));
        }

        public static IEnumerable<string> GetAll(string tag)
            => GetAll(tag.ToIdentifier());

        public static IEnumerable<string> GetAll(Identifier tag)
        {
            return TextPacks[GameSettings.CurrentConfig.Language]
                .SelectMany(p => p.Texts.TryGetValue(tag, out var value)
                    ? (IEnumerable<string>)value
                    : Array.Empty<string>());
        }
        
        public static IEnumerable<KeyValuePair<Identifier, string>> GetAllTagTextPairs()
        {
            return TextPacks[GameSettings.CurrentConfig.Language]
                .SelectMany(p => p.Texts)
                .SelectMany(kvp => kvp.Value.Select(v => new KeyValuePair<Identifier, string>(kvp.Key, v)));
        }

        public static IEnumerable<string> GetTextFiles()
        {
            return GetTextFilesRecursive(Path.Combine("Content", "Texts"));
        }

        private static IEnumerable<string> GetTextFilesRecursive(string directory)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                yield return file.CleanUpPath();
            }
            foreach (string subDir in Directory.GetDirectories(directory))
            {
                foreach (string file in GetTextFilesRecursive(subDir))
                {
                    yield return file;
                }
            }
        }

        public static string GetTranslatedLanguageName(LanguageIdentifier languageIdentifier)
        {
            return TextPacks[languageIdentifier].First().TranslatedName;
        }

        public static void ClearCache()
        {
            lock (cachedStrings)
            {
                cachedStrings.Clear();
                nonCacheableTags.Clear();
            }
        }

        public static LocalizedString Get(params Identifier[] tags)
        {
            TagLString? str = null;
            lock (cachedStrings)
            {
                if (tags.Length == 1 && !nonCacheableTags.Contains(tags[0]))
                {
                    var tag = tags[0];
                    if (cachedStrings.TryGetValue(tag, out var strRef))
                    {
                        if (!strRef.TryGetTarget(out str))
                        {
                            cachedStrings.Remove(tag);
                        }
                    }

                    if (str is null && TextPacks.ContainsKey(GameSettings.CurrentConfig.Language))
                    {
                        int count = 0;
                        foreach (var pack in TextPacks[GameSettings.CurrentConfig.Language])
                        {
                            if (pack.Texts.TryGetValue(tag, out var texts))
                            {
                                count += texts.Length;
                                if (count > 1) { break; }
                            }
                        }

                        if (count > 1)
                        {
                            nonCacheableTags = nonCacheableTags.Add(tag);
                        }
                        else
                        {
                            str = new TagLString(tags);
                            cachedStrings.Add(tag, new WeakReference<TagLString>(str));
                        }
                    }
                }
            }
            return str ?? new TagLString(tags);
        }
        
        public static LocalizedString Get(params string[] tags)
            => Get(tags.ToIdentifiers());

        public static LocalizedString AddPunctuation(char punctuationSymbol, params LocalizedString[] texts)
        {
            return new AddedPunctuationLString(punctuationSymbol, texts);
        }

        public static LocalizedString GetFormatted(Identifier tag, params object[] args)
        {
            return GetFormatted(new TagLString(tag), args);
        }

        public static LocalizedString GetFormatted(LocalizedString str, params object[] args)
        {
            LocalizedString[] argStrs = new LocalizedString[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is LocalizedString ls) { argStrs[i] = ls; }
                else { argStrs[i] = new RawLString(args[i].ToString() ?? ""); }
            }
            return new FormattedLString(str, argStrs);
        }

        public static string FormatServerMessage(string str) => $"{str}~";
        
        public static string FormatServerMessage(string message, params (string Key, string Value)[] keysWithValues)
        {
            if (keysWithValues.Length == 0)
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
                string.Join("", keysWithValues.Select((kv, index) => kv.Value.IndexOfAny(new char[] { '~', '/' }) != -1 ? $"~{kv.Key}=[{textId}.{index}]" : $"~{kv.Key}={kv.Value}"))
            );
        }
        
        internal static string FormatServerMessageWithPronouns(CharacterInfo charInfo, string message, params (string Key, string Value)[] keysWithValues)
        {
            var pronounCategory = charInfo.Prefab.Pronouns;
            (string Key, string Value)[] pronounKwv = new[]
            {
                ("[PronounLowercase]", charInfo.ReplaceVars($"Pronoun[{pronounCategory}]Lowercase")),
                ("[PronounUppercase]", charInfo.ReplaceVars($"Pronoun[{pronounCategory}]")),
                ("[PronounPossessiveLowercase]", charInfo.ReplaceVars($"PronounPossessive[{pronounCategory}]Lowercase")),
                ("[PronounPossessiveUppercase]", charInfo.ReplaceVars($"PronounPossessive[{pronounCategory}]")),
                ("[PronounReflexiveLowercase]", charInfo.ReplaceVars($"PronounReflexive[{pronounCategory}]Lowercase")),
                ("[PronounReflexiveUppercase]", charInfo.ReplaceVars($"PronounReflexive[{pronounCategory}]"))
            };
            return FormatServerMessage(message, keysWithValues.Concat(pronounKwv).ToArray());
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
        
        public static LocalizedString ParseInputTypes(LocalizedString str, bool useColorHighlight = false)
        {
            return new InputTypeLString(str, useColorHighlight);
        }

        public static LocalizedString GetWithVariable(string tag, string varName, LocalizedString value, FormatCapitals formatCapitals = FormatCapitals.No)
        {
            return GetWithVariable(tag.ToIdentifier(), varName.ToIdentifier(), value, formatCapitals);
        }

        public static LocalizedString GetWithVariable(Identifier tag, Identifier varName, LocalizedString value, FormatCapitals formatCapitals = FormatCapitals.No)
        {
            return GetWithVariables(tag, (varName, value));
        }

        public static LocalizedString GetWithVariables(string tag, params (string Key, string Value)[] replacements)
        {
            return GetWithVariables(
                tag.ToIdentifier(),
                replacements.Select(kv =>
                    (kv.Key.ToIdentifier(),
                    (LocalizedString)new RawLString(kv.Value),
                    FormatCapitals.No)));
        }

        public static LocalizedString GetWithVariables(string tag, params (string Key, LocalizedString Value)[] replacements)
        {
            return GetWithVariables(
                tag.ToIdentifier(),
                replacements.Select(kv =>
                    (kv.Key.ToIdentifier(),
                    kv.Value,
                    FormatCapitals.No)));
        }

        public static LocalizedString GetWithVariables(string tag, params (string Key, LocalizedString Value, FormatCapitals FormatCapitals)[] replacements)
        {
            return GetWithVariables(
                tag.ToIdentifier(),
                replacements.Select(kv =>
                    (kv.Key.ToIdentifier(),
                     kv.Value,
                     kv.FormatCapitals)));
        }

        public static LocalizedString GetWithVariables(string tag, params (string Key, string Value, FormatCapitals FormatCapitals)[] replacements)
        {
            return GetWithVariables(
                tag.ToIdentifier(),
                replacements.Select(kv =>
                    (kv.Key.ToIdentifier(),
                    (LocalizedString)new RawLString(kv.Value),
                    kv.FormatCapitals)));
        }

        public static LocalizedString GetWithVariables(Identifier tag, params (Identifier Key, LocalizedString Value)[] replacements)
        {
            return GetWithVariables(tag, replacements.Select(kv => (kv.Key, kv.Value, FormatCapitals.No)));
        }

        public static LocalizedString GetWithVariables(Identifier tag, IEnumerable<(Identifier, LocalizedString, FormatCapitals)> replacements)
        {
            return new ReplaceLString(new TagLString(tag), StringComparison.OrdinalIgnoreCase, replacements);
        }
        
        public static void ConstructDescription(ref LocalizedString description, XElement descriptionElement)
        {
            /*
            <Description tag="talentdescription.simultaneousskillgain">
                <Replace tag="[skillname1]" value="skillname.helm"/>
                <Replace tag="[skillname2]" value="skillname.weapons"/>
                <Replace tag="[somevalue]" value="45.3"/>
            </Description>
            */

            LocalizedString extraDescriptionLine = Get(descriptionElement.GetAttributeIdentifier("tag", Identifier.Empty));
            foreach (XElement replaceElement in descriptionElement.Elements())
            {
                if (replaceElement.NameAsIdentifier() != "replace") { continue; }

                Identifier tag = replaceElement.GetAttributeIdentifier("tag", Identifier.Empty);
                string[] replacementValues = replaceElement.GetAttributeStringArray("value", Array.Empty<string>());
                LocalizedString replacementValue = string.Empty;
                for (int i = 0; i < replacementValues.Length; i++)
                {
                    replacementValue += Get(replacementValues[i]).Fallback(replacementValues[i]);
                    if (i < replacementValues.Length - 1)
                    {
                        replacementValue += ", ";
                    }
                }
                if (replaceElement.Attribute("color") != null)
                {
                    string colorStr = replaceElement.GetAttributeString("color", "255,255,255,255");
                    replacementValue = $"‖color:{colorStr}‖" + replacementValue + "‖color:end‖";
                }
                extraDescriptionLine = extraDescriptionLine.Replace(tag, replacementValue);
            }
            if (!(description is RawLString { Value: "" })) { description += "\n"; } //TODO: this is cursed
            description += extraDescriptionLine;
        }

        public static LocalizedString FormatCurrency(int amount)
        {
             return GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount));
        }

        public static LocalizedString GetServerMessage(string serverMessage)
        {
            return new ServerMsgLString(serverMessage);
        }

        public static LocalizedString Capitalize(this LocalizedString str)
        {
            return new CapitalizeLString(str);
        }

        public static void IncrementLanguageVersion()
        {
            LanguageVersion++;
            ClearCache();
        }
        
#if DEBUG
        public static void CheckForDuplicates(LanguageIdentifier lang)
        {
            if (!TextPacks.ContainsKey(lang))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + lang + ")!");
                return;
            }

            int packIndex = 0;
            foreach (TextPack textPack in TextPacks[lang])
            {
                textPack.CheckForDuplicates(packIndex);
                packIndex++;
            }
        }

        public static void WriteToCSV()
        {
            LanguageIdentifier lang = DefaultLanguage;

            if (!TextPacks.ContainsKey(lang))
            {
                DebugConsole.ThrowError("No text packs available for the selected language (" + lang + ")!");
                return;
            }

            int packIndex = 0;
            foreach (TextPack textPack in TextPacks[lang])
            {
                textPack.WriteToCSV(packIndex);
                packIndex++;
            }
        }
#endif
    }
}