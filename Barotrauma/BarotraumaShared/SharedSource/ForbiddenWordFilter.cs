using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma
{
    static class ForbiddenWordFilter
    {
        static readonly string fileListPath = Path.Combine("Data", "forbiddenwordlist.txt");

        private static readonly HashSet<string> forbiddenWords;

        static ForbiddenWordFilter()
        {
            try
            {
                forbiddenWords = File.ReadAllLines(fileListPath).ToHashSet();
            }
            catch (IOException e)
            {
                DebugConsole.ThrowError($"Failed to load the list of forbidden words from {fileListPath}.", e);
            }
        }

        public static bool IsForbidden(string text)
        {
            return IsForbidden(text, out _);
        }

        public static bool IsForbidden(string text, out string forbiddenWord)
        {
            forbiddenWord = string.Empty;
            if (forbiddenWords == null)
            {
                return false;
            }

            char[] delimiters = new char[] { ' ', '-', '.', '_', ':', ';', '\'' };

            HashSet<string> words = new HashSet<string>();
            foreach (char delimiter in delimiters)
            {
                foreach (string word in text.Split(delimiter))
                {
                    words.Add(word);
                }
            }

            foreach (string word in words)
            {
                if (forbiddenWords.Any(w => Homoglyphs.Compare(word, w) || Homoglyphs.Compare(word + 's', w)))
                {
                    forbiddenWord = word;
                    return true;
                }
            }
            return false;
        }
    }
}
