using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public static class XMLExtensions
    {
        public static XDocument TryLoadXml(string filePath)
        {
            XDocument doc;
            try
            {
                ToolBox.IsProperFilenameCase(filePath);
                doc = XDocument.Load(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't load xml document \"" + filePath + "\"!", e);
                return null;
            }

            if (doc.Root == null) return null;

            return doc;
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
            if (value.Contains(".") && Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatVal))
            {
                return floatVal;
            }
            if (Int32.TryParse(value, out intVal))
            {
                return intVal;
            }
            
            string lowerTrimmedVal = value.ToLowerInvariant().Trim();
            if (lowerTrimmedVal == "true")
            {
                return true;
            }
            if (lowerTrimmedVal == "false")
            {
                return false;
            }

            return value;
        }


        public static string GetAttributeString(this XElement element, string name, string defaultValue)
        {
            if (element?.Attribute(name) == null) return defaultValue;
            return GetAttributeString(element.Attribute(name), defaultValue);
        }

        private static string GetAttributeString(XAttribute attribute, string defaultValue)
        {
            string value = attribute.Value;
            return String.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public static float GetAttributeFloat(this XElement element, float defaultValue, params string[] matchingAttributeName)
        {
            if (element == null) return defaultValue;

            foreach (string name in matchingAttributeName)
            {
                if (element.Attribute(name) == null) continue;

                float val;

                try
                {
                    if (!Single.TryParse(element.Attribute(name).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + "!", e);
                    continue;
                }

                return val;
            }

            return defaultValue;
        }

        public static float GetAttributeFloat(this XElement element, string name, float defaultValue)
        {
            if (element?.Attribute(name) == null) return defaultValue;

            float val = defaultValue;

            try
            {
                if (!Single.TryParse(element.Attribute(name).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                {
                    return defaultValue;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "!", e);
            }

            return val;
        }

        public static float GetAttributeFloat(this XAttribute attribute, float defaultValue)
        {
            if (attribute == null) return defaultValue;

            float val = defaultValue;

            try
            {
                val = Single.Parse(attribute.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + attribute + "! ", e);
            }

            return val;
        }

        public static int GetAttributeInt(this XElement element, string name, int defaultValue)
        {
            if (element?.Attribute(name) == null) return defaultValue;

            int val = defaultValue;

            try
            {
                val = Int32.Parse(element.Attribute(name).Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + element + "! ", e);
            }

            return val;
        }

        public static bool GetAttributeBool(this XElement element, string name, bool defaultValue)
        {
            if (element?.Attribute(name) == null) return defaultValue;

            return element.Attribute(name).GetAttributeBool(defaultValue);
        }

        public static bool GetAttributeBool(this XAttribute attribute, bool defaultValue)
        {
            if (attribute == null) return defaultValue;

            string val = attribute.Value.ToLowerInvariant().Trim();
            if (val == "true")
            {
                return true;
            }
            if (val == "false")
            {
                return false;
            }

            DebugConsole.ThrowError("Error in " + attribute.Value.ToString() + "! \"" + val + "\" is not a valid boolean value");
            return false;
        }

        public static Vector2 GetAttributeVector2(this XElement element, string name, Vector2 defaultValue)
        {
            if (element?.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseVector2(val);
        }

        public static Vector3 GetAttributeVector3(this XElement element, string name, Vector3 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseVector3(val);
        }

        public static Vector4 GetAttributeVector4(this XElement element, string name, Vector4 defaultValue)
        {
            if (element == null || element.Attribute(name) == null) return defaultValue;

            string val = element.Attribute(name).Value;

            return ParseVector4(val);
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

        public static string Vector2ToString(Vector2 vector)
        {
            return vector.X.ToString("G", CultureInfo.InvariantCulture) + "," + vector.Y.ToString("G", CultureInfo.InvariantCulture);
        }

        public static string Vector3ToString(Vector3 vector, string format = "G")
        {
            return vector.X.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Y.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Z.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string Vector4ToString(Vector4 vector, string format = "G")
        {
            return vector.X.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Y.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.Z.ToString(format, CultureInfo.InvariantCulture) + "," +
                   vector.W.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string ColorToString(Color color)
        {
            return (color.R / 255.0f).ToString("G", CultureInfo.InvariantCulture) + "," +
                    (color.G / 255.0f).ToString("G", CultureInfo.InvariantCulture) + "," +
                    (color.B / 255.0f).ToString("G", CultureInfo.InvariantCulture) + "," +
                    (color.A / 255.0f).ToString("G", CultureInfo.InvariantCulture);
        }

        public static string RectToString(Rectangle rect)
        {
            return rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height;
        }

        public static Vector2 ParseVector2(string stringVector2, bool errorMessages = true)
        {
            string[] components = stringVector2.Split(',');

            Vector2 vector = Vector2.Zero;

            if (components.Length != 2)
            {
                if (!errorMessages) return vector;
                DebugConsole.ThrowError("Failed to parse the string \"" + stringVector2 + "\" to Vector2");
                return vector;
            }

            float.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.X);
            float.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Y);

            return vector;
        }

        public static Vector3 ParseVector3(string stringVector3, bool errorMessages = true)
        {
            string[] components = stringVector3.Split(',');

            Vector3 vector = Vector3.Zero;

            if (components.Length != 3)
            {
                if (!errorMessages) return vector;
                DebugConsole.ThrowError("Failed to parse the string \"" + stringVector3 + "\" to Vector3");
                return vector;
            }

            Single.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.X);
            Single.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Y);
            Single.TryParse(components[2], NumberStyles.Any, CultureInfo.InvariantCulture, out vector.Z);

            return vector;
        }

        public static Vector4 ParseVector4(string stringVector4, bool errorMessages = true)
        {
            string[] components = stringVector4.Split(',');

            Vector4 vector = Vector4.Zero;

            if (components.Length < 3)
            {
                if (errorMessages) DebugConsole.ThrowError("Failed to parse the string \"" + stringVector4 + "\" to Vector4");
                return vector;
            }

            Single.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.X);
            Single.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Y);
            Single.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Z);
            if (components.Length > 3)
                Single.TryParse(components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.W);

            return vector;
        }

        public static Color ParseColor(string stringColor, bool errorMessages = true)
        {
            string[] strComponents = stringColor.Split(',');

            Color color = Color.White;

            if (strComponents.Length < 3)
            {
                if (errorMessages) DebugConsole.ThrowError("Failed to parse the string \"" + stringColor + "\" to Color");
                return Color.White;
            }

            float[] components = new float[4] { 1.0f, 1.0f, 1.0f, 1.0f };
            
            for (int i = 0; i < 4 && i < strComponents.Length; i++)
            {
                float.TryParse(strComponents[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]);
            }

            return new Color(components[0], components[1], components[2], components[3]);
        }

        public static Rectangle ParseRect(string stringColor, bool errorMessages = true)
        {
            string[] strComponents = stringColor.Split(',');
            if (strComponents.Length < 3)
            {
                if (errorMessages) DebugConsole.ThrowError("Failed to parse the string \"" + stringColor + "\" to Color");
                return new Rectangle(0, 0, 0, 0);
            }

            int[] components = new int[4] { 0, 0, 0, 0 };
            for (int i = 0; i < 4 && i < strComponents.Length; i++)
            {
                int.TryParse(strComponents[i], out components[i]);
            }

            return new Rectangle(components[0], components[1], components[2], components[3]);
        }

        public static float[] ParseFloatArray(string[] stringArray)
        {
            if (stringArray == null || stringArray.Length == 0) return null;

            float[] floatArray = new float[stringArray.Length];
            for (int i = 0; i < floatArray.Length; i++)
            {
                floatArray[i] = 0.0f;
                Single.TryParse(stringArray[i], NumberStyles.Float, CultureInfo.InvariantCulture, out floatArray[i]);
            }

            return floatArray;
        }
    }
}
