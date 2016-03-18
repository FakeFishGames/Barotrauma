using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    public static class ToolBox
    {
        public static XDocument TryLoadXml(string filePath)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't load xml document ''"+filePath+"''!", e);
                return null;
            }

            if (doc.Root == null) return null;

            return doc;
        }

        public static SpriteFont TryLoadFont(string file, Microsoft.Xna.Framework.Content.ContentManager contentManager)
        {
            SpriteFont font = null;
            try
            {
                font = contentManager.Load<SpriteFont>(file);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading font ''"+file+"'' failed", e);
            }

            return font;
        }

        public static object GetAttributeObject(XElement element, string name)
        {            
            if (element.Attribute(name) == null) return null;
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
                string lowerTrimmedVal = value.ToLower().Trim();
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
            if (element.Attribute(name) == null) return defaultValue;
            return GetAttributeString(element.Attribute(name), defaultValue);
        }

        public static string GetAttributeString(XAttribute attribute, string defaultValue)
        {
            string value = attribute.Value;
            if (String.IsNullOrEmpty(value)) return defaultValue;
            return value;
        }

        public static float GetAttributeFloat(XElement element, string name, float defaultValue)
        {
            if (element.Attribute(name) == null) return defaultValue;

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
            if (element.Attribute(name) == null) return defaultValue;

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
            var attribute = element.Attribute(name);
            if (attribute == null) return defaultValue;

            return GetAttributeBool(attribute, defaultValue);
        }

        public static bool GetAttributeBool(XAttribute attribute, bool defaultValue)
        {
            if (attribute == null) return defaultValue;

            string val = attribute.Value.ToLower().Trim();
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
                DebugConsole.ThrowError("Error in " + attribute.Value.ToString() + "! ''" + val + "'' is not a valid boolean value");
                return false;
            }
        }



        public static Vector2 GetAttributeVector2(XElement element, string name, Vector2 defaultValue)
        {
            if (element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseToVector2(val);
        }
        
        public static Vector4 GetAttributeVector4(XElement element, string name, Vector4 defaultValue)
        {
            if (element.Attribute(name) == null) return defaultValue;

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
                DebugConsole.ThrowError("Failed to parse the string ''"+stringVector2+"'' to Vector2");
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

        public static Vector4 ParseToVector4(string stringVector4, bool errorMessages = true)
        {
            string[] components = stringVector4.Split(',');

            Vector4 vector = Vector4.Zero;

            if (components.Length < 3)
            {
                if (errorMessages) DebugConsole.ThrowError("Failed to parse the string ''" + stringVector4 + "'' to Vector4");
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
                asciiBytes[i % 4] |= asciiBytes[i];
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
        
        public static string WrapText(string text, float lineLength, SpriteFont font)
        {
            if (font.MeasureString(text).X < lineLength) return text;

            text = text.Replace("\n", " \n ");

            string[] words = text.Split(' ');//, '\n');

            StringBuilder wrappedText = new StringBuilder();
            float linePos = 0f;
            float spaceWidth = font.MeasureString(" ").X;
            for (int i = 0; i < words.Length; ++i)
            {
                if (string.IsNullOrWhiteSpace(words[i]) && words[i]!="\n") continue;

                Vector2 size;
                string tempWord = words[i];
                string prevWord = words[i];
                while ((size = font.MeasureString(tempWord)).X > lineLength)
                {
                    tempWord = tempWord.Remove(tempWord.Length - 1, 1);
                }

                words[i] = tempWord;
                if (prevWord.Length> tempWord.Length)
                {
                    wrappedText.Append(words[i]);
                    wrappedText.Append(" \n");
                    wrappedText.Append(prevWord.Remove(0, tempWord.Length));
                    linePos = lineLength*2.0f;
                    continue;

                }
                
                if (linePos + size.X < lineLength)
                {
                        wrappedText.Append(words[i]);
                    if (words[i] == "\n")
                    {
                        linePos = 0.0f;
                    }
                    else
                    {

                        linePos += size.X + spaceWidth;
                    }
                }
                else
                {
                    //if (i>0)wrappedText.Remove(wrappedText.Length - 1, 1);

                        wrappedText.Append("\n");
                        wrappedText.Append(words[i]);
                    
                        linePos = size.X + spaceWidth;
                    
                }

                if (i < words.Length - 1) wrappedText.Append(" ");
            }

            return wrappedText.ToString();
        }

        public static string GetRandomLine(string filePath)
        {
            try
            {
                string randomLine = "";
                StreamReader file = new StreamReader(filePath);                

                var lines = File.ReadLines(filePath).ToList();
                int lineCount = lines.Count();

                if (lineCount == 0)
                {
                    DebugConsole.ThrowError("File ''" + filePath + "'' is empty!");
                    file.Close();
                    return "";
                }

                int lineNumber = Rand.Int(lineCount, false);

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
                DebugConsole.ThrowError("Couldn't open file ''" + filePath + "''!", e);

                return "";
            }            
        }
    }
}
