using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public static class TextManager
    {
        //key = language
        private static Dictionary<string, List<TextPack>> textPacks = new Dictionary<string, List<TextPack>>();

        public static string Language;

        public static IEnumerable<string> AvailableLanguages
        {
            get { return textPacks.Keys; }
        }
        
        public static void LoadTextPacks(string directory)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                try
                {
                    var textPack = new TextPack(file);
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
            foreach (string subDir in Directory.GetDirectories(directory))
            {
                LoadTextPacks(subDir);
            }
        }

        public static string Get(string textTag)
        {
            if (string.IsNullOrWhiteSpace(Language))
            {
                DebugConsole.ThrowError("Failed to get text \"" + textTag + "\" (language not set)!");
                return textTag;
            }

            if (!textPacks.ContainsKey(Language))
            {
                DebugConsole.ThrowError("Failed to get text \"" + textTag + "\" (no text files configured for the language \"" + Language + "\")!");
                return textTag;
            }

            foreach (TextPack textPack in textPacks[Language])
            {
                string text = textPack.Get(textTag);
                if (text != null) return text;
            }

            DebugConsole.ThrowError("Text \"" + textTag + "\" not found");
            return textTag;
        }

        public static string ReplaceGenderPronouns(string text, Gender gender)
        {
            if (gender == Gender.Male)
            {
                return text.Replace("[genderpronoun]",     Get("PronounMale").ToLower())
                    .Replace("[genderpronounpossessive]",  Get("PronounPossessiveMale").ToLower())
                    .Replace("[genderpronounreflexive]",   Get("PronounReflexiveMale").ToLower())
                    .Replace("[Genderpronoun]",            Capitalize(Get("PronounMale")))
                    .Replace("[Genderpronounpossessive]",  Capitalize(Get("PronounPossessiveMale")))
                    .Replace("[Genderpronounreflexive]",   Capitalize(Get("PronounReflexiveMale")));
            }
            else
            {
                return text.Replace("[genderpronoun]",     Get("PronounFemale").ToLower())
                    .Replace("[genderpronounpossessive]",  Get("PronounPossessiveFemale").ToLower())
                    .Replace("[genderpronounreflexive]",   Get("PronounReflexiveFemale").ToLower())
                    .Replace("[Genderpronoun]",            Capitalize(Get("PronounFemale")))
                    .Replace("[Genderpronounpossessive]",  Capitalize(Get("PronounPossessiveFemale")))
                    .Replace("[Genderpronounreflexive]",   Capitalize(Get("PronounReflexiveFemale")));
            }
        }

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
