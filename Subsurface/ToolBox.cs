using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.IO.Compression;

namespace Subsurface
{
    static class ToolBox
    {
        public static Vector2 SmoothStep(Vector2 v1, Vector2 v2, float amount)
        {
            return new Vector2(
                 MathHelper.SmoothStep(v1.X, v2.X, amount),
                 MathHelper.SmoothStep(v1.Y, v2.Y, amount));
        }

        public static float Round(float value, float div)
        {
            return (float)Math.Floor(value / div) * div;
        }

        public static float RandomFloat(float minimum, float maximum)
        {
            return (float)Game1.random.NextDouble() * (maximum - minimum) + minimum;
        }

        public static int RandomInt(int minimum, int maximum)
        {
            return Game1.random.Next(maximum - minimum) + minimum;
        }

        public static float RandomFloatLocal(float minimum, float maximum)
        {
            return (float)Game1.localRandom.NextDouble() * (maximum - minimum) + minimum;
        }

        public static int RandomIntLocal(int minimum, int maximum)
        {
            return Game1.localRandom.Next(maximum - minimum) + minimum;
        }

        public static float VectorToAngle(Vector2 vector)
        {
            return (float)Math.Atan2(vector.Y, vector.X);
        }

        public static float CurveAngle(float from, float to, float step)
        {
            // Ensure that 0 <= angle < 2pi for both "from" and "to" 
            while (from < 0)
                from += MathHelper.TwoPi;
            while (from >= MathHelper.TwoPi)
                from -= MathHelper.TwoPi;

            while (to < 0)
                to += MathHelper.TwoPi;
            while (to >= MathHelper.TwoPi)
                to -= MathHelper.TwoPi;

            if (Math.Abs(from - to) < MathHelper.Pi)
            {
                // The simple case - a straight lerp will do. 
                return MathHelper.Lerp(from, to, step);
            }

            // If we get here we have the more complex case. 
            // First, increment the lesser value to be greater. 
            if (from < to)
                from += MathHelper.TwoPi;
            else
                to += MathHelper.TwoPi;

            float retVal = MathHelper.Lerp(from, to, step);

            // Now ensure the return value is between 0 and 2pi 
            if (retVal >= MathHelper.TwoPi)
                retVal -= MathHelper.TwoPi;
            return retVal;
        }

        public static float WrapAngleTwoPi(float angle)
        {
            // Ensure that 0 <= angle < 2pi for both "from" and "to" 
            while (angle < 0)
                angle += MathHelper.TwoPi;
            while (angle >= MathHelper.TwoPi)
                angle -= MathHelper.TwoPi;

            return angle;
        }

        public static float WrapAnglePi(float angle)
        {
            // Ensure that -pi <= angle < pi for both "from" and "to" 
            while (angle < -MathHelper.Pi)
                angle += MathHelper.TwoPi;
            while (angle >= MathHelper.Pi)
                angle -= MathHelper.TwoPi;

            return angle;
        }

        public static float GetShortestAngle(float from, float to)
        {
            // Ensure that 0 <= angle < 2pi for both "from" and "to" 
            from = WrapAngleTwoPi(from);
            to = WrapAngleTwoPi(to);

            if (Math.Abs(from - to) < MathHelper.Pi)
            {
                return to - from;
            }

            // If we get here we have the more complex case. 
            // First, increment the lesser value to be greater. 
            if (from < to)
                from += MathHelper.TwoPi;
            else
                to += MathHelper.TwoPi;

            return to - from;
        }

        /// <summary>
        /// solves the angle opposite to side a (parameters: lengths of each side)
        /// </summary>
        public static float SolveTriangleSSS(float a, float b, float c)
        {
            float A = (float)Math.Acos((b*b + c*c - a*a) / (2*b*c));

            if (float.IsNaN(A)) A = 1.0f;

            return A;
        }


        //public static void CompressStringToFile(string fileName, string value)
        //{
        //    // A.
        //    // Write string to temporary file.
        //    string temp = Path.GetTempFileName();
        //    File.WriteAllText(temp, value);

        //    // B.
        //    // Read file into byte array buffer.
        //    byte[] b;
        //    using (FileStream f = new FileStream(temp, FileMode.Open))
        //    {
        //        b = new byte[f.Length];
        //        f.Read(b, 0, (int)f.Length);
        //    }

        //    // C.
        //    // Use GZipStream to write compressed bytes to target file.
        //    using (FileStream f2 = new FileStream(fileName, FileMode.Create))
        //    using (GZipStream gz = new GZipStream(f2, CompressionMode.Compress, false))
        //    {
        //        gz.Write(b, 0, b.Length);
        //    }
        //}

        //public static Stream DecompressFiletoStream(string fileName)
        //{
        //    if (!File.Exists(fileName))
        //    {
        //        DebugConsole.ThrowError("File ''"+fileName+" doesn't exist!");
        //        return null;
        //    }

        //    using (FileStream originalFileStream = new FileStream(fileName, FileMode.Open))
        //    {
        //        MemoryStream decompressedFileStream = new MemoryStream();
                
        //        using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
        //        {
        //            decompressionStream.CopyTo(decompressedFileStream);
        //            return decompressedFileStream;
        //        }				
        //    }
        //}

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
            if (element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value.ToLower().Trim();
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
                DebugConsole.ThrowError("Error in " + element + "! ''" + val + "'' is not a valid boolean value");
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

        public static Vector2 ParseToVector2(string stringVector2)
        {
            string[] components = stringVector2.Split(',');

            Vector2 vector = Vector2.Zero;

            if (components.Length!=2)
            {
                DebugConsole.ThrowError("Failed to parse the string "+stringVector2+" to Vector2");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.X);            
            float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Y);

            return vector;
        }

        public static string Vector2ToString(Vector2 vector)
        {
            return vector.X.ToString("G", CultureInfo.InvariantCulture) + "," + vector.Y.ToString("G", CultureInfo.InvariantCulture);
        }



        public static Vector4 ParseToVector4(string stringVector4)
        {
            string[] components = stringVector4.Split(',');

            Vector4 vector = Vector4.Zero;

            if (components.Length < 3)
            {
                DebugConsole.ThrowError("Failed to parse the string " + stringVector4 + " to Vector4");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.X);
            float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Y);
            float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Z);
            if (components.Length>3)
                float.TryParse(components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.W);

            return vector;
        }

        public static string Vector4ToString(Vector4 vector)
        {
            return vector.X.ToString("G", CultureInfo.InvariantCulture) + "," +
                    vector.Y.ToString("G", CultureInfo.InvariantCulture) + "," +
                    vector.Z.ToString("G", CultureInfo.InvariantCulture) + "," +
                    vector.W.ToString("G", CultureInfo.InvariantCulture);
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

        public static string WrapText(string text, float lineWidth)
        {
            if (GUI.font.MeasureString(text).X < lineWidth) return text;

            string[] words = text.Split(' ');
            StringBuilder wrappedText = new StringBuilder();
            float linewidth = 0f;
            float spaceWidth = GUI.font.MeasureString(" ").X;
            for (int i = 0; i < words.Length; ++i)
            {
                Vector2 size = GUI.font.MeasureString(words[i]);
                if (linewidth + size.X < lineWidth)
                {
                    linewidth += size.X + spaceWidth;
                }
                else
                {
                    wrappedText.Append("\n");
                    linewidth = size.X + spaceWidth;
                }
                wrappedText.Append(words[i]);
                if (i<words.Length-1) wrappedText.Append(" ");
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

                int lineNumber = Game1.random.Next(lineCount);

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


        
        public static byte AngleToByte(float angle)
        {
            angle = WrapAngleTwoPi(angle);
            angle = angle * (255.0f / MathHelper.TwoPi); 
            return Convert.ToByte(angle);
        }

        public static float ByteToAngle(byte b)
        {
            float angle = (float)b;
            angle = angle * (MathHelper.TwoPi / 255.0f);
            return angle;
        }

    }
}
