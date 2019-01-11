using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public static class TextManager
    {
        private static List<TextPack> textPacks = new List<TextPack>();

        public static string Language;

        private static HashSet<string> availableLanguages = new HashSet<string>();
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
       
        public static void LoadTextPacks(string directory)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                try
                {
                    var textPack = new TextPack(file);
                    availableLanguages.Add(textPack.Language);
                    if (textPack.Language == Language) textPacks.Add(textPack);
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

        public static string Get(string textTag, bool returnNull = false)
        {
            foreach (TextPack textPack in textPacks)
            {
                string text = textPack.Get(textTag);
                if (text != null) return text;
            }

            if (returnNull)
            {
                return null;
            }
            else
            {
                DebugConsole.ThrowError("Text \"" + textTag + "\" not found");
                return textTag;
            }
        }

        public static string Get(string textTag, bool returnNull = false, params object[] args)
        {
            foreach (TextPack textPack in textPacks)
            {
                string text = textPack.Get(textTag);
                if (text != null)
                {
                    text = string.Format(text, args);
                    return text;
                }
            }

            if (returnNull)
            {
                return null;
            }
            else
            {
                DebugConsole.ThrowError("Text \"" + textTag + "\" not found");
                return textTag;
            }
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
