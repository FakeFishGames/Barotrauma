using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using File = Barotrauma.IO.File;
using FileStream = Barotrauma.IO.FileStream;
using Path = Barotrauma.IO.Path;

namespace Barotrauma
{
    public static class XMLExtensions
    {
        private static ImmutableDictionary<Type, Func<string, object, object>> converters
            = new Dictionary<Type, Func<string, object, object>>()
            {
                { typeof(string), (str, defVal) => str },
                { typeof(int), (str, defVal) => int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : defVal },
                { typeof(uint), (str, defVal) => uint.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out uint result) ? result : defVal },
                { typeof(UInt64), (str, defVal) => UInt64.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out UInt64 result) ? result : defVal },
                { typeof(float), (str, defVal) => float.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out float result) ? result : defVal },
                { typeof(bool), (str, defVal) => bool.TryParse(str, out bool result) ? result : defVal },
                { typeof(Color), (str, defVal) => ParseColor(str) },
                { typeof(Vector2), (str, defVal) => ParseVector2(str) },
                { typeof(Vector3), (str, defVal) => ParseVector3(str) },
                { typeof(Vector4), (str, defVal) => ParseVector4(str) },
                { typeof(Rectangle), (str, defVal) => ParseRect(str, true) }
            }.ToImmutableDictionary();
        
        public static string ParseContentPathFromUri(this XObject element)
            => !string.IsNullOrWhiteSpace(element.BaseUri)
                ? System.IO.Path.GetRelativePath(Environment.CurrentDirectory, element.BaseUri.CleanUpPath())
                : "";

        public static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
        };

        public static XmlReader CreateReader(System.IO.Stream stream, string baseUri = "")
            => XmlReader.Create(stream, ReaderSettings, baseUri);
        
        public static XDocument TryLoadXml(System.IO.Stream stream)
        {
            XDocument doc;
            try
            {
                using XmlReader reader = CreateReader(stream);
                doc = XDocument.Load(reader);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Couldn't load xml document from stream!", e);
                return null;
            }
            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("XML could not be loaded from stream: Document or the root element is invalid!");
                return null;
            }
            return doc;
        }
        
        public static XDocument TryLoadXml(ContentPath path) => TryLoadXml(path.Value);
        
        public static XDocument TryLoadXml(string filePath)
        {
            var doc = TryLoadXml(filePath, out var exception);
            if (exception != null)
            {
                DebugConsole.ThrowError($"Couldn't load xml document \"{filePath}\"!", exception);
            }
            else if (doc is null)
            {
                DebugConsole.ThrowError($"File \"{filePath}\" could not be loaded: Document or the root element is invalid!");
            }
            return doc;
        }

        public static XDocument TryLoadXml(string filePath, out Exception exception)
        {
            exception = null;
            XDocument doc;
            try
            {
                ToolBox.IsProperFilenameCase(filePath);
                using FileStream stream = File.Open(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                using XmlReader reader = CreateReader(stream, Path.GetFullPath(filePath));
                doc = XDocument.Load(reader, LoadOptions.SetBaseUri);
            }
            catch (Exception e)
            {
                exception = e;
                return null;
            }
            if (doc?.Root == null)
            {
                return null;
            }
            return doc;
        }

        public static object GetAttributeObject(XAttribute attribute)
        {
            if (attribute == null) { return null; }

            return ParseToObject(attribute.Value.ToString());
        }

        public static object ParseToObject(string value)
        {
            if (value.Contains(".") && Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
            {
                return floatVal;
            }
            if (Int32.TryParse(value, out int intVal))
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
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            string str = GetAttributeString(attribute, defaultValue);
#if DEBUG
            if (!str.IsNullOrEmpty() &&
                (str.Contains("%ModDir", StringComparison.OrdinalIgnoreCase)
                 || str.CleanUpPathCrossPlatform(correctFilenameCase: false).StartsWith("Content/", StringComparison.OrdinalIgnoreCase)))
            {
                DebugConsole.ThrowError($"Use {nameof(GetAttributeContentPath)} instead of {nameof(GetAttributeString)}\n{Environment.StackTrace.CleanupStackTrace()}");
                if (Debugger.IsAttached) { Debugger.Break(); }
            }
#endif
            return str;
        }

        public static string GetAttributeStringUnrestricted(this XElement element, string name, string defaultValue)
        {
            #warning TODO: remove?
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return GetAttributeString(attribute, defaultValue);
        }

        public static bool DoesAttributeReferenceFileNameAlone(this XElement element, string name)
        {
            string texName = element.GetAttributeStringUnrestricted(name, "");
            return !texName.IsNullOrEmpty() & !texName.Contains("/") && !texName.Contains("%ModDir", StringComparison.OrdinalIgnoreCase);
        }

        public static ContentPath GetAttributeContentPath(this XElement element, string name, ContentPackage contentPackage)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return null; }
            return ContentPath.FromRaw(contentPackage, GetAttributeString(attribute, null));
        }
        
        public static Identifier GetAttributeIdentifier(this XElement element, string name, string defaultValue)
        {
            return element.GetAttributeString(name, defaultValue).ToIdentifier();
        }

        public static Identifier GetAttributeIdentifier(this XElement element, string name, Identifier defaultValue)
        {
            return element.GetAttributeIdentifier(name, defaultValue.Value);
        }

        private static string GetAttributeString(XAttribute attribute, string defaultValue)
        {
            string value = attribute.Value;
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public static string[] GetAttributeStringArray(this XElement element, string name, string[] defaultValue, bool trim = true, bool convertToLowerInvariant = false)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            string stringValue = attribute.Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',', '，');

            for (int i = 0; i < splitValue.Length; i++)
            {
                if (convertToLowerInvariant) { splitValue[i] = splitValue[i].ToLowerInvariant(); }
                if (trim) { splitValue[i] = splitValue[i].Trim(); }
            }

            return splitValue;
        }

        public static Identifier[] GetAttributeIdentifierArray(this XElement element, string name, Identifier[] defaultValue, bool trim = true)
        {
            return element.GetAttributeStringArray(name, null, trim: trim, convertToLowerInvariant: false)
                    ?.ToIdentifiers()
                ?? defaultValue;
        }

        public static float GetAttributeFloat(this XElement element, float defaultValue, params string[] matchingAttributeName)
        {
            if (element == null) { return defaultValue; }

            foreach (string name in matchingAttributeName)
            {
                var attribute = element.GetAttribute(name);
                if (attribute == null) { continue; }
                return GetAttributeFloat(attribute, defaultValue);
            }

            return defaultValue;
        }

        public static float GetAttributeFloat(this XElement element, string name, float defaultValue) => GetAttributeFloat(element?.GetAttribute(name), defaultValue);

        public static float GetAttributeFloat(this XAttribute attribute, float defaultValue)
        {
            if (attribute == null) { return defaultValue; }

            float val = defaultValue;

            try
            {
                string strVal = attribute.Value;
                if (strVal.LastOrDefault() == 'f')
                {
                    strVal = strVal.Substring(0, strVal.Length - 1);
                }
                val = float.Parse(strVal, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + attribute + "! ", e);
            }

            return val;
        }

        public static double GetAttributeDouble(this XElement element, string name, double defaultValue) => GetAttributeDouble(element?.GetAttribute(name), defaultValue);

        public static double GetAttributeDouble(this XAttribute attribute, double defaultValue)
        {
            if (attribute == null) { return defaultValue; }

            double val = defaultValue;
            try
            {
                string strVal = attribute.Value;
                if (strVal.LastOrDefault() == 'f')
                {
                    strVal = strVal.Substring(0, strVal.Length - 1);
                }
                val = double.Parse(strVal, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in " + attribute + "!", e);
            }

            return val;
        }


        public static float[] GetAttributeFloatArray(this XElement element, string name, float[] defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            string stringValue = attribute.Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',');
            float[] floatValue = new float[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    string strVal = splitValue[i];
                    if (strVal.LastOrDefault() == 'f')
                    {
                        strVal = strVal.Substring(0, strVal.Length - 1);
                    }
                    floatValue[i] = float.Parse(strVal, CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
                }
            }

            return floatValue;
        }

        public static bool TryGetAttributeInt(this XElement element, string name, out int result)
        {
            var attribute = element?.GetAttribute(name);
            result = default;
            if (attribute == null) { return false; }

            if (int.TryParse(attribute.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intVal))
            {
                result = intVal;
                return true;
            }
            if (float.TryParse(attribute.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var floatVal))
            {
                result = (int)floatVal;
                return true;
            }
            return false;
        }
        
        public static int GetAttributeInt(this XElement element, string name, int defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            int val = defaultValue;

            try
            {
                if (!Int32.TryParse(attribute.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                {
                    val = (int)float.Parse(element.GetAttribute(name).Value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
            }

            return val;
        }

        public static uint GetAttributeUInt(this XElement element, string name, uint defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            uint val = defaultValue;

            try
            {
                val = UInt32.Parse(attribute.Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
            }

            return val;
        }

        public static UInt64 GetAttributeUInt64(this XElement element, string name, UInt64 defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            UInt64 val = defaultValue;

            try
            {
                val = UInt64.Parse(attribute.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
            }

            return val;
        }

        public static Version GetAttributeVersion(this XElement element, string name, Version defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            Version val = defaultValue;

            try
            {
                val = Version.Parse(attribute.Value);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
            }

            return val;
        }

        public static int[] GetAttributeIntArray(this XElement element, string name, int[] defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            string stringValue = attribute.Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',');
            int[] intValue = new int[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    int val = Int32.Parse(splitValue[i]);
                    intValue[i] = val;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
                }
            }

            return intValue;
        }
        public static ushort[] GetAttributeUshortArray(this XElement element, string name, ushort[] defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            string stringValue = attribute.Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(',');
            ushort[] ushortValue = new ushort[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    ushort val = ushort.Parse(splitValue[i]);
                    ushortValue[i] = val;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
                }
            }

            return ushortValue;
        }

        public static T GetAttributeEnum<T>(this XElement element, string name, T defaultValue) where T : struct, Enum
        {
            var attr = element?.GetAttribute(name);
            if (attr == null) { return defaultValue; }
            return Enum.TryParse(attr.Value, true, out T result) ? result :
                   int.TryParse(attr.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int resultInt) ? Unsafe.As<int, T>(ref resultInt) :
                   defaultValue;
        }

        public static bool GetAttributeBool(this XElement element, string name, bool defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return attribute.GetAttributeBool(defaultValue);
        }

        public static bool GetAttributeBool(this XAttribute attribute, bool defaultValue)
        {
            if (attribute == null) { return defaultValue; }

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

        public static Point GetAttributePoint(this XElement element, string name, Point defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return ParsePoint(attribute.Value);
        }

        public static Vector2 GetAttributeVector2(this XElement element, string name, Vector2 defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return ParseVector2(attribute.Value);
        }

        public static Vector3 GetAttributeVector3(this XElement element, string name, Vector3 defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return ParseVector3(attribute.Value);
        }

        public static Vector4 GetAttributeVector4(this XElement element, string name, Vector4 defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return ParseVector4(attribute.Value);
        }

        public static Color GetAttributeColor(this XElement element, string name, Color defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return ParseColor(attribute.Value);
        }

        public static Color? GetAttributeColor(this XElement element, string name)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return null; }
            return ParseColor(attribute.Value);
        }

        public static Color[] GetAttributeColorArray(this XElement element, string name, Color[] defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            string stringValue = attribute.Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            string[] splitValue = stringValue.Split(';');
            Color[] colorValue = new Color[splitValue.Length];
            for (int i = 0; i < splitValue.Length; i++)
            {
                try
                {
                    Color val = ParseColor(splitValue[i], true);
                    colorValue[i] = val;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error when reading attribute \"{name}\" from {element}!", e);
                }
            }

            return colorValue;
        }

#if CLIENT
        public static KeyOrMouse GetAttributeKeyOrMouse(this XElement element, string name, KeyOrMouse defaultValue)
        {
            string strValue = element.GetAttributeString(name, defaultValue?.ToString() ?? "");
            if (Enum.TryParse(strValue, true, out Microsoft.Xna.Framework.Input.Keys key))
            {
                return key;
            }
            else if (Enum.TryParse(strValue, out MouseButton mouseButton))
            {
                return mouseButton;
            }
            else if (int.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out int mouseButtonInt) &&
                     (Enum.GetValues(typeof(MouseButton)) as MouseButton[]).Contains((MouseButton)mouseButtonInt))
            {
                return (MouseButton)mouseButtonInt;
            }
            return defaultValue;
        }
#endif

        public static Rectangle GetAttributeRect(this XElement element, string name, Rectangle defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }
            return ParseRect(attribute.Value, false);
        }

        //TODO: nested tuples and and n-uples where n!=2 are unsupported
        public static (T1, T2) GetAttributeTuple<T1, T2>(this XElement element, string name, (T1, T2) defaultValue)
        {
            string strValue = element.GetAttributeString(name, $"({defaultValue.Item1}, {defaultValue.Item2})").Trim();

            return ParseTuple(strValue, defaultValue);
        }

        public static (T1, T2)[] GetAttributeTupleArray<T1, T2>(this XElement element, string name,
            (T1, T2)[] defaultValue)
        {
            var attribute = element?.GetAttribute(name);
            if (attribute == null) { return defaultValue; }

            string stringValue = attribute.Value;
            if (string.IsNullOrEmpty(stringValue)) { return defaultValue; }

            return stringValue.Split(';').Select(s => ParseTuple<T1, T2>(s, default)).ToArray();
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

        public static string PointToString(Point point)
        {
            return point.X.ToString() + "," + point.Y.ToString();
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

        [Obsolete("Prefer XMLExtensions.ToStringHex")]
        public static string ColorToString(Color color)
            => $"{color.R},{color.G},{color.B},{color.A}";

        public static string ToStringHex(this Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                + ((color.A < 255) ? $"{color.A:X2}" : "");

        public static string RectToString(Rectangle rect)
        {
            return rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height;
        }

        public static (T1, T2) ParseTuple<T1, T2>(string strValue, (T1, T2) defaultValue)
        {
            strValue = strValue.Trim();
            //require parentheses
            if (strValue[0] != '(' || strValue[^1] != ')') { return defaultValue; }
            //remove parentheses
            strValue = strValue[1..^1];

            string[] elems = strValue.Split(',');
            if (elems.Length != 2) { return defaultValue; }
            
            return ((T1)converters[typeof(T1)].Invoke(elems[0], defaultValue.Item1),
                (T2)converters[typeof(T2)].Invoke(elems[1], defaultValue.Item2));
        }
        
        public static Point ParsePoint(string stringPoint, bool errorMessages = true)
        {
            string[] components = stringPoint.Split(',');
            Point point = Point.Zero;

            if (components.Length != 2)
            {
                if (!errorMessages) { return point; }
                DebugConsole.ThrowError("Failed to parse the string \"" + stringPoint + "\" to Vector2");
                return point;
            }

            int.TryParse(components[0], NumberStyles.Any, CultureInfo.InvariantCulture, out point.X);
            int.TryParse(components[1], NumberStyles.Any, CultureInfo.InvariantCulture, out point.Y);
            return point;
        }

        public static Vector2 ParseVector2(string stringVector2, bool errorMessages = true)
        {
            string[] components = stringVector2.Split(',');

            Vector2 vector = Vector2.Zero;

            if (components.Length != 2)
            {
                if (!errorMessages) { return vector; }
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
                if (!errorMessages) { return vector; }
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
                if (errorMessages) { DebugConsole.ThrowError("Failed to parse the string \"" + stringVector4 + "\" to Vector4"); }
                return vector;
            }

            Single.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.X);
            Single.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Y);
            Single.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.Z);
            if (components.Length > 3)
            {
                Single.TryParse(components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vector.W);
            }

            return vector;
        }

        public static Color ParseColor(string stringColor, bool errorMessages = true)
        {
            if (stringColor.StartsWith("gui.", StringComparison.OrdinalIgnoreCase))
            {
#if CLIENT
                Identifier colorName = stringColor.Substring(4).ToIdentifier();
                if (GUIStyle.Colors.TryGetValue(colorName, out GUIColor guiColor))
                {
                    return guiColor.Value;
                }
#endif
                return Color.White;
            }


            string[] strComponents = stringColor.Split(',');

            Color color = Color.White;

            float[] components = new float[4] { 1.0f, 1.0f, 1.0f, 1.0f };

            if (strComponents.Length == 1)
            {
                bool altParseFailed = true;
                stringColor = stringColor.Trim();
                if (stringColor.Length > 0 && stringColor[0] == '#')
                {
                    stringColor = stringColor.Substring(1);

                    if (int.TryParse(stringColor, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int colorInt))
                    {
                        if (stringColor.Length == 6)
                        {
                            colorInt = (colorInt << 8) | 0xff;
                        }
                        components[0] = ((float)((colorInt & 0xff000000) >> 24)) / 255.0f;
                        components[1] = ((float)((colorInt & 0x00ff0000) >> 16)) / 255.0f;
                        components[2] = ((float)((colorInt & 0x0000ff00) >> 8)) / 255.0f;
                        components[3] = ((float)(colorInt & 0x000000ff)) / 255.0f;

                        altParseFailed = false;
                    }
                }
                else if (stringColor.Length > 0 && stringColor[0] == '{')
                {
                    stringColor = stringColor.Substring(1, stringColor.Length-2);

                    string[] mgComponents = stringColor.Split(' ');
                    if (mgComponents.Length == 4)
                    {
                        altParseFailed = false;
                        
                        string[] expectedPrefixes = {"R:", "G:", "B:", "A:"};
                        for (int i = 0; i < 4; i++)
                        {
                            if (mgComponents[i].StartsWith(expectedPrefixes[i], StringComparison.OrdinalIgnoreCase))
                            {
                                string strToParse = mgComponents[i]
                                    .Remove(expectedPrefixes[i], StringComparison.OrdinalIgnoreCase)
                                    .Trim();
                                int val = 0;
                                altParseFailed |= !int.TryParse(strToParse, out val);
                                components[i] = ((float) val) / 255f;
                            }
                            else
                            {
                                altParseFailed = true;
                                break;
                            }
                        }
                    }
                }

                if (altParseFailed)
                {
                    if (errorMessages) { DebugConsole.ThrowError("Failed to parse the string \"" + stringColor + "\" to Color"); }
                    return Color.White;
                }
            }
            else
            {
                for (int i = 0; i < 4 && i < strComponents.Length; i++)
                {
                    float.TryParse(strComponents[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]);
                }

                if (components.Any(c => c > 1.0f))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        components[i] = components[i] / 255.0f;
                    }
                    //alpha defaults to 1.0 if not given
                    if (strComponents.Length < 4) components[3] = 1.0f;
                }
            }

            return new Color(components[0], components[1], components[2], components[3]);
        }
        
        public static Rectangle ParseRect(string stringRect, bool requireSize, bool errorMessages = true)
        {
            string[] strComponents = stringRect.Split(',');
            if ((strComponents.Length < 3 && requireSize) || strComponents.Length < 2)
            {
                if (errorMessages) { DebugConsole.ThrowError("Failed to parse the string \"" + stringRect + "\" to Rectangle"); }
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

        public static Identifier VariantOf(this XElement element) =>
            element.GetAttributeIdentifier("inherit", element.GetAttributeIdentifier("variantof", ""));

        public static string[] ParseStringArray(string stringArrayValues)
        {
            return string.IsNullOrEmpty(stringArrayValues) ? Array.Empty<string>() : stringArrayValues.Split(';');
        }
        
        public static Identifier[] ParseIdentifierArray(string stringArrayValues)
        {
            return ParseStringArray(stringArrayValues).ToIdentifiers().ToArray();
        }

        public static bool IsOverride(this XElement element) => element.NameAsIdentifier() == "override";

        public static XElement FirstElement(this XElement element) => element.Elements().FirstOrDefault();

        public static XAttribute GetAttribute(this XElement element, string name, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => element.GetAttribute(a => a.Name.ToString().Equals(name, comparisonMethod));

        public static void SetAttributeValue(this XElement element, string name, object value, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => GetAttribute(element, name, comparisonMethod)?.SetValue(value);

        public static XAttribute GetAttribute(this XElement element, Identifier name) => element.GetAttribute(name.Value, StringComparison.OrdinalIgnoreCase);

        public static XAttribute GetAttribute(this XElement element, Func<XAttribute, bool> predicate) => element.Attributes().FirstOrDefault(predicate);

        /// <summary>
        /// Returns the first child element that matches the name using the provided comparison method.
        /// </summary>
        public static XElement GetChildElement(this XContainer container, string name, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => container.Elements().FirstOrDefault(e => e.Name.ToString().Equals(name, comparisonMethod));

        /// <summary>
        /// Returns all child elements that match the name using the provided comparison method.
        /// </summary>
        public static IEnumerable<XElement> GetChildElements(this XContainer container, string name, StringComparison comparisonMethod = StringComparison.OrdinalIgnoreCase) => container.Elements().Where(e => e.Name.ToString().Equals(name, comparisonMethod));

        public static IEnumerable<XElement> GetChildElements(this XContainer container, params string[] names)
        {
            return names.SelectMany(name => container.GetChildElements(name));
        }

        public static bool ComesAfter(this XElement element, XElement other)
        {
            if (element.Parent != other.Parent) { return false; }
            foreach (var child in element.Parent.Elements())
            {
                if (child == element) { return false; }
                if (child == other) { return true; }
            }
            return false;
        }

        public static Identifier NameAsIdentifier(this XElement elem)
        {
            return elem.Name.LocalName.ToIdentifier();
        }

        public static Identifier NameAsIdentifier(this XAttribute attr)
        {
            return attr.Name.LocalName.ToIdentifier();
        }
    }
}
