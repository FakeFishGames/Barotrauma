using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;

namespace Barotrauma
{
    public class Pair<T1, T2>
    {
        public T1 First { get; set; }
        public T2 Second { get; set; }

        public Pair(T1 first, T2 second)
        {
            First  = first;
            Second = second;
        }
    }

    public class Triplet<T1, T2, T3>
    {
        public T1 First { get; set; }
        public T2 Second { get; set; }
        public T3 Third { get; set; }

        public Triplet(T1 first, T2 second, T3 third)
        {
            First = first;
            Second = second;
            Third = third;
        }
    }

    public static partial class ToolBox
    {
        private static List<string> contentPath = new List<string>(5);
        public static bool IsProperFilenameCase(string filename)
        {
            //File case only matters on Linux where the filesystem is case-sensitive, so we don't need these errors in release builds.
            //It also seems Path.GetFullPath may return a path with an incorrect case on Windows when the case of any of the game's
            //parent folders have been changed.
#if !DEBUG && !LINUX
            return true;
#endif
            char[] delimiters = { '/', '\\' };
            string[] subDirs = filename.Split(delimiters);
            string originalFilename = filename;

            // Only take the game content directory into account

            // The algorithm
            //var contentPath = subDirs.Reverse().TakeWhile(d => d != "Content").ToList();
            //contentPath.Add("Content");
            //subDirs = subDirs.Where(d => contentPath.Contains(d)).ToArray();

            // Optimized but quite unreadable (and error-prone) implementation, which should in practice do the same that the one above.
            // I usually avoid this kind of code, but here it's possible that the check is performed very often when loading a lot of files.
            contentPath.Clear();
            for (int i = subDirs.Length - 1; i >= 0; i--)
            {
                string subDir = subDirs[i];
                contentPath.Add(subDir);
                if (subDir == "Content") { break; }
            }
            subDirs = new string[contentPath.Count];
            for (int i = 0; i < subDirs.Length; i++)
            {
                subDirs[i] = contentPath[subDirs.Length - (i + 1)];
            }

            filename = "";

            for (int i = 0; i < subDirs.Length - 1; i++)
            {
                filename += subDirs[i] + "/";

                if (i == subDirs.Length - 2)
                {
                    string[] filePaths = Directory.GetFiles(filename);
                    if (filePaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.Ordinal)))
                    {
                        return true;
                    }
                    else if (filePaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.OrdinalIgnoreCase)))
                    {
                        DebugConsole.ThrowError(originalFilename + " has incorrect case!");
                        return false;
                    }
                }

                string[] dirPaths = Directory.GetDirectories(filename);

                if (!dirPaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.Ordinal)))
                {
                    if (dirPaths.Any(s => s.Equals(filename + subDirs[i + 1], StringComparison.OrdinalIgnoreCase)))
                    {
                        DebugConsole.ThrowError(originalFilename + " has incorrect case!");
                    }
                    else
                    {
                        DebugConsole.ThrowError(originalFilename + " doesn't exist!");
                    }
                    return false;
                }
            }
            return true;
        }

        public static string LimitString(string str, int maxCharacters)
        {
            if (str == null || maxCharacters < 0) return null;

            if (maxCharacters < 4 || str.Length <= maxCharacters) return str;

            return str.Substring(0, maxCharacters - 3) + "...";
        }

        public static string RandomSeed(int length)
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(
                Enumerable.Repeat(chars, length)
                          .Select(s => s[Rand.Int(s.Length)])
                          .ToArray());
        }

        public static int StringToInt(string str)
        {
            str = str.Substring(0, Math.Min(str.Length, 32));

            str = str.PadLeft(4, 'a');

            byte[] asciiBytes = Encoding.ASCII.GetBytes(str);

            for (int i = 4; i < asciiBytes.Length; i++)
            {
                asciiBytes[i % 4] ^= asciiBytes[i];
            }

            return BitConverter.ToInt32(asciiBytes, 0);
        }
        /// <summary>
        /// a method for changing inputtypes with old names to the new ones to ensure backwards compatibility with older subs
        /// </summary>
        public static string ConvertInputType(string inputType)
        {
            if (inputType == "ActionHit" || inputType == "Action") return "Use";
            if (inputType == "SecondaryHit" || inputType == "Secondary") return "Aim";

            return inputType;
        }

        /// <summary>
        /// Calculates the minimum number of single-character edits (i.e. insertions, deletions or substitutions) required to change one string into the other
        /// </summary>
        public static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0 || m == 0) return 0;

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        public static string SecondsToReadableTime(float seconds)
        {
            if (seconds < 60.0f)
            {
                return (int)seconds + " s";
            }
            else
            {
                int m = (int)(seconds / 60.0f);
                int s = (int)(seconds % 60.0f);

                return s == 0 ?
                    m + " m" :
                    m + " m " + s + " s";
            }
        }

        private static Dictionary<string, List<string>> cachedLines = new Dictionary<string, List<string>>();
        public static string GetRandomLine(string filePath)
        {
            List<string> lines;
            if (cachedLines.ContainsKey(filePath))
            {
                lines = cachedLines[filePath];
            }
            else
            {
                try
                {
                    using (StreamReader file = new StreamReader(filePath))
                    {
                        lines = File.ReadLines(filePath).ToList();
                        cachedLines.Add(filePath, lines);
                        if (lines.Count == 0)
                        {
                            DebugConsole.ThrowError("File \"" + filePath + "\" is empty!");
                            return "";
                        }
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Couldn't open file \"" + filePath + "\"!", e);
                    return "";
                }
            }

            if (lines.Count == 0) return "";
            return lines[Rand.Range(0, lines.Count, Rand.RandSync.Server)];
        }

        /// <summary>
        /// Reads a number of bits from the buffer and inserts them to a new NetBuffer instance
        /// </summary>
        public static NetBuffer ExtractBits(this NetBuffer originalBuffer, int numberOfBits)
        {
            var buffer = new NetBuffer();
            byte[] data = new byte[(int)Math.Ceiling(numberOfBits / (double)8)];

            originalBuffer.ReadBits(data, 0, numberOfBits);
            buffer.Write(data);

            return buffer;
        }

        public static T SelectWeightedRandom<T>(IList<T> objects, IList<float> weights, Rand.RandSync randSync)
        {
            return SelectWeightedRandom(objects, weights, Rand.GetRNG(randSync));
        }

        public static T SelectWeightedRandom<T>(IList<T> objects, IList<float> weights, Random random)
        {
            if (objects.Count == 0) return default(T);

            if (objects.Count != weights.Count)
            {
                DebugConsole.ThrowError("Error in SelectWeightedRandom, number of objects does not match the number of weights.\n" + Environment.StackTrace);
                return objects[0];
            }

            float totalWeight = weights.Sum();

            float randomNum = (float)(random.NextDouble() * totalWeight);
            for (int i = 0; i < objects.Count; i++)
            {
                if (randomNum <= weights[i])
                {
                    return objects[i];
                }
                randomNum -= weights[i];
            }
            return default(T);
        }

        public static UInt32 StringToUInt32Hash(string str, MD5 md5)
        {
            //calculate key based on MD5 hash instead of string.GetHashCode
            //to ensure consistent results across platforms
            byte[] inputBytes = Encoding.ASCII.GetBytes(str);
            byte[] hash = md5.ComputeHash(inputBytes);

            UInt32 key = (UInt32)((str.Length & 0xff) << 24); //could use more of the hash here instead?
            key |= (UInt32)(hash[hash.Length - 3] << 16);
            key |= (UInt32)(hash[hash.Length - 2] << 8);
            key |= (UInt32)(hash[hash.Length - 1]);

            return key;
        }
        /// <summary>
        /// Returns a new instance of the class with all properties and fields copied.
        /// </summary>
        public static T CreateCopy<T>(this T source, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public) where T : new() => CopyValues(source, new T(), flags);
        public static T CopyValuesTo<T>(this T source, T target, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public) => CopyValues(source, target, flags);

        /// <summary>
        /// Copies the values of the source to the destination. May not work, if the source is of higher inheritance class than the destination. Does not work with virtual properties.
        /// </summary>
        public static T CopyValues<T>(T source, T destination, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public)
        {
            if (source == null)
            {
                throw new Exception("Failed to copy object. Source is null.");
            }
            if (destination == null)
            {
                throw new Exception("Failed to copy object. Destination is null.");
            }
            Type type = source.GetType();
            var properties = type.GetProperties(flags);
            foreach (var property in properties)
            {
                if (property.CanWrite)
                {
                    property.SetValue(destination, property.GetValue(source, null), null);
                }
            }
            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                field.SetValue(destination, field.GetValue(source));
            }
            // Check that the fields match.Uncomment to apply the test, if in doubt.
            //if (fields.Any(f => { var value = f.GetValue(destination); return value == null || !value.Equals(f.GetValue(source)); }))
            //{
            //    throw new Exception("Failed to copy some of the fields.");
            //}
            return destination;
        }
    }
}
