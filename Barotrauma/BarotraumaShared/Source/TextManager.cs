using System;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    public static class TextManager
    {
        //only used if none of the selected content packages contain any text files
        const string VanillaTextFilePath = "Content/Texts/EnglishVanilla.xml";

        //key = language
        private static Dictionary<string, List<TextPack>> textPacks = new Dictionary<string, List<TextPack>>();

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
        }

        public static string Get(string textTag, bool returnNull = false)
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
                if (text != null) return text;
            }

            //if text was not found and we're using a language other than English, see if we can find an English version
            //may happen, for example, if a user has selected another language and using mods that haven't been translated to that language
            if (Language != "English" && textPacks.ContainsKey("English"))
            {
                foreach (TextPack textPack in textPacks["English"])
                {
                    string text = textPack.Get(textTag);
                    if (text != null) return text;
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

        public static string Get(string textTag, bool returnNull = false, params object[] args)
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
                if (text != null)
                {
                    text = string.Format(text, args);
                    return text;
                }
            }

            if (Language != "English" && textPacks.ContainsKey("English"))
            {
                foreach (TextPack textPack in textPacks[Language])
                {
                    string text = textPack.Get(textTag);
                    if (text != null)
                    {
                        text = string.Format(text, args);
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
