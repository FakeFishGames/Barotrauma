using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    public class Pair<T1, T2>
    {
        public T1 First { get; set; }
        public T2 Second { get; set; }

        public static Pair<T1, T2> Create(T1 first, T2 second)
        {
            Pair<T1, T2> pair = new Pair<T1, T2>();
            pair.First  = first;
            pair.Second = second;

            return pair;
        }
    }

    public static partial class ToolBox
    {
        public static bool IsProperFilenameCase(string filename)
        {
            char[] delimiters = { '/','\\' };
            string[] subDirs = filename.Split(delimiters);
            string originalFilename = filename;
            filename = "";

            for (int i=0;i<subDirs.Length-1;i++)
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

                if (!dirPaths.Any(s => s.Equals(filename+subDirs[i+1],StringComparison.Ordinal)))
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

        public static XDocument TryLoadXml(string filePath)
        {
            XDocument doc;
            try
            {
                IsProperFilenameCase(filePath);
                doc = XDocument.Load(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't load xml document \""+filePath+"\"!", e);
                return null;
            }

            if (doc.Root == null) return null;

            return doc;
        }

        /*public static SpriteFont TryLoadFont(string file, Microsoft.Xna.Framework.Content.ContentManager contentManager)
        {
            SpriteFont font = null;
            try
            {
                font = contentManager.Load<SpriteFont>(file);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading font \""+file+"\" failed", e);
            }

            return font;
        }*/

        public static object GetAttributeObject(XElement element, string name)
        {
            if (element == null || element.Attribute(name) == null) return null;
            return GetAttributeObject(element.Attribute(name));
        }

        public static object GetAttributeObject(XAttribute attribute)
        {
            if (attribute == null) return null;

            return ParseToObject(attribute.Value.ToString());
        }

        public static object ParseToObject(string value)
        {
            float floatVal;
            int intVal;
            if (value.ToString().Contains(".") && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatVal))
            {
                return floatVal;
            }
            else if (int.TryParse(value, out intVal))
            {
                return intVal;
            }
            else
            {
                string lowerTrimmedVal = value.ToLowerInvariant().Trim();
                if (lowerTrimmedVal == "true")
                {
                    return true;
                }
                else if (lowerTrimmedVal == "false")
                {
                    return false;
                }
                else
                {
                    return value;
                }
            }
        }


        public static string GetAttributeString(XElement element, string name, string defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;
            return GetAttributeString(element.Attribute(name), defaultValue);
        }

        public static string GetAttributeString(XAttribute attribute, string defaultValue)
        {
            string value = attribute.Value;
            if (String.IsNullOrEmpty(value)) return defaultValue;
            return value;
        }

        public static float GetAttributeFloat(XElement element, float defaultValue, params string[] matchingAttributeName)
        {
            if (element == null) return defaultValue;

            foreach (string name in matchingAttributeName)
            {
                if (element.Attribute(name) == null) continue;

                float val = defaultValue;

                try
                {
                    if (!float.TryParse(element.Attribute(name).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in "+element+"!", e);
                    continue;
                }
            
                return val;   
            }

            return defaultValue;
        }

        public static float GetAttributeFloat(XElement element, string name, float defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            float val = defaultValue;

            try
            {
                if (!float.TryParse(element.Attribute(name).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                {
                    return defaultValue;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in "+element+"!", e);
            }
            
            return val;
        }

        public static float GetAttributeFloat(XAttribute attribute, float defaultValue)
        {
            if (attribute == null) return defaultValue;

            float val = defaultValue;

            try
            {
                val = float.Parse(attribute.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + attribute + "! ", e);
            }

            return val;
        }

        public static int GetAttributeInt(XElement element, string name, int defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            int val = defaultValue;

            try
            {
                val = int.Parse(element.Attribute(name).Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "! ", e);
            }

            return val;
        }
        
        public static bool GetAttributeBool(XElement element, string name, bool defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            return GetAttributeBool(element.Attribute(name), defaultValue);
        }

        public static bool GetAttributeBool(XAttribute attribute, bool defaultValue)
        {
            if (attribute == null) return defaultValue;

            string val = attribute.Value.ToLowerInvariant().Trim();
            if (val == "true")
            {
                return true;
            }
            else if (val == "false")
            {
                return false;
            }
            else
            {
                DebugConsole.ThrowError("Error in " + attribute.Value.ToString() + "! \"" + val + "\" is not a valid boolean value");
                return false;
            }
        }



        public static Vector2 GetAttributeVector2(XElement element, string name, Vector2 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseToVector2(val);
        }

        public static Vector3 GetAttributeVector3(XElement element, string name, Vector3 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseToVector3(val);
        }
        
        public static Vector4 GetAttributeVector4(XElement element, string name, Vector4 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseToVector4(val);
        }

        public static string ElementInnerText(this XElement el)
        {
            StringBuilder str = new StringBuilder();
            foreach (XNode element in el.DescendantNodes().Where(x => x.NodeType == XmlNodeType.Text))
            {
                str.Append(element.ToString());
            }
            return str.ToString();
        }




        public static Vector2 ParseToVector2(string stringVector2, bool errorMessages = true)
        {
            string[] components = stringVector2.Split(',');

            Vector2 vector = Vector2.Zero;

            if (components.Length!=2)
            {
                if (!errorMessages) return vector;
                DebugConsole.ThrowError("Failed to parse the string \""+stringVector2+"\" to Vector2");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.X);            
            float.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Y);

            return vector;
        }

        public static string Vector2ToString(Vector2 vector)
        {
            return vector.X.ToString("G", CultureInfo.InvariantCulture) + "," + vector.Y.ToString("G", CultureInfo.InvariantCulture);
        }

        public static Vector3 ParseToVector3(string stringVector3, bool errorMessages = true)
        {
            string[] components = stringVector3.Split(',');

            Vector3 vector = Vector3.Zero;

            if (components.Length!=3)
            {
                if (!errorMessages) return vector;
                DebugConsole.ThrowError("Failed to parse the string \""+stringVector3+"\" to Vector3");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.X);            
            float.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Y);
            float.TryParse(components[2], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Z);

            return vector;
        }

        public static Vector4 ParseToVector4(string stringVector4, bool errorMessages = true)
        {
            string[] components = stringVector4.Split(',');

            Vector4 vector = Vector4.Zero;

            if (components.Length < 3)
            {
                if (errorMessages) DebugConsole.ThrowError("Failed to parse the string \"" + stringVector4 + "\" to Vector4");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.X);
            float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Y);
            float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Z);
            if (components.Length>3)
                float.TryParse(components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.W);

            return vector;
        }

        public static string Vector4ToString(Vector4 vector, string format = "G")
        {
            return vector.X.ToString(format, CultureInfo.InvariantCulture) + "," +
                    vector.Y.ToString(format, CultureInfo.InvariantCulture) + "," +
                    vector.Z.ToString(format, CultureInfo.InvariantCulture) + "," +
                    vector.W.ToString(format, CultureInfo.InvariantCulture);
        }

        public static float[] ParseArrayToFloat(string[] stringArray)
        {
            if (stringArray == null || stringArray.Length == 0) return null;

            float[] floatArray = new float[stringArray.Length];
            for (int i = 0; i<floatArray.Length; i++)
            {
                floatArray[i]=0.0f;
                float.TryParse(stringArray[i], NumberStyles.Float, CultureInfo.InvariantCulture, out floatArray[i]);
            }

            return floatArray;
        }

        public static string LimitString(string str, int maxCharacters)
        {
            if (str == null || maxCharacters < 0) return null;

            if (maxCharacters < 4 || str.Length <= maxCharacters) return str;
            
            return str.Substring(0, maxCharacters-3) + "...";            
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
            if (inputType == "SecondarHit" || inputType == "Secondary") return "Aim";

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

            for (int i = 0; i <= n; d[i, 0] = i++);
            for (int j = 0; j <= m; d[0, j] = j++);

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

        public static string GetRandomLine(string filePath)
        {
            try
            {
                string randomLine = "";
                StreamReader file = new StreamReader(filePath);                

                var lines = File.ReadLines(filePath).ToList();
                int lineCount = lines.Count;

                if (lineCount == 0)
                {
                    DebugConsole.ThrowError("File \"" + filePath + "\" is empty!");
                    file.Close();
                    return "";
                }

                int lineNumber = Rand.Int(lineCount, Rand.RandSync.Server);

                int i = 0;
                    
                foreach (string line in lines)
                {
                    if (i == lineNumber)
                    {
                        randomLine = line;
                        break;
                    }
                    i++;
                }

                file.Close();
                
                return randomLine;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open file \"" + filePath + "\"!", e);

                return "";
            }            
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
    }
}
