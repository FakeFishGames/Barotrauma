#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Barotrauma
{
    public static class StructSerialization
    {
        private static readonly ImmutableDictionary<Type, MethodInfo> deserializeMethods;
        private static readonly ImmutableDictionary<Type, MethodInfo> serializeMethods;
        
        public class SkipAttribute : Attribute { }

        private static bool ShouldSkip(this FieldInfo field)
            => field.GetCustomAttribute<SkipAttribute>() != null;

        private static HandlerAttribute? ExtractHandler(this FieldInfo field)
            => field.GetCustomAttribute<HandlerAttribute>();
        
        public class HandlerAttribute : Attribute
        {
            public readonly Func<string?, object?> Read;
            public readonly Func<object?, string?> Write;
            
            public HandlerAttribute(Type handlerType)
            {
                var readAction =
                    handlerType.GetMethod(nameof(Read), BindingFlags.Public | BindingFlags.Static)
                    ?? throw new Exception($"Type {handlerType.Name} does not have a static {nameof(Read)} method");
                var writeAction = 
                    handlerType.GetMethod(nameof(Write), BindingFlags.Public | BindingFlags.Static)
                    ?? throw new Exception($"Type {handlerType.Name} does not have a static {nameof(Write)} method");
                var paramArray = new object?[1];
                Read = (s) =>
                {
                    paramArray[0] = s;
                    return readAction.Invoke(null, paramArray);
                };
                Write = (o) =>
                {
                    paramArray[0] = o;
                    return writeAction.Invoke(null, paramArray)?.ToString();
                };
            }
        }
        
        static StructSerialization()
        {
            deserializeMethods =
                typeof(StructSerialization)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(m =>
                {
                    if (!m.Name.StartsWith("Deserialize")) { return false; }
                    var parameters = m.GetParameters();
                    if (parameters.Length < 1 || parameters.Length > 2 ||
                        parameters[0].ParameterType != typeof(string))
                    {
                        return false;
                    }
                    return true;
                })
                .Select(m => (m.ReturnType, m))
                .ToImmutableDictionary();
            
            serializeMethods =
                typeof(StructSerialization)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(m =>
                    {
                        if (!m.Name.StartsWith("Serialize")) { return false; }
                        var parameters = m.GetParameters();
                        if (parameters.Length != 1 ||
                            m.ReturnType != typeof(string))
                        {
                            return false;
                        }
                        return true;
                    })
                    .Select(m => (m.GetParameters()[0].ParameterType, m))
                    .ToImmutableDictionary();
        }

        public static void CopyPropertiesFrom<T>(this ref T self, in T other) where T : struct
        {
            var fields = self.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly).ToArray();
            foreach (var field in fields)
            {
                if (field.ShouldSkip()) { continue; }
                field.SetValue(self, field.GetValue(other));
            }
        }

        public static void DeserializeElement<T>(this ref T self, XElement element) where T : struct
        {
            var fields = self.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).ToArray();

            //box the struct here so we don't end up
            //making a copy every time we feed this to reflection
            object boxedSelf = self;
            foreach (var field in fields)
            {
                if (field.ShouldSkip()) { continue; }
                boxedSelf.TryDeserialize(field, element);
            }
            //copy the boxed struct into the original
            self = (T)boxedSelf;
        }

        public static void TryDeserialize(this object boxedSelf, FieldInfo field, XElement element)
        {
            string fieldName = field.Name.ToLowerInvariant();
            string valueStr = element.GetAttributeString(fieldName, field.GetValue(boxedSelf)?.ToString() ?? "");

            var handler = field.ExtractHandler();

            if (handler != null)
            {
                field.SetValue(boxedSelf, handler.Read(valueStr));
            }
            else if (deserializeMethods.TryGetValue(field.FieldType, out MethodInfo? deserializeMethod))
            {
                object?[] parameters = { valueStr };
                if (deserializeMethod.GetParameters().Length > 1)
                {
                    Array.Resize(ref parameters, 2);
                    parameters[1] = field.GetValue(boxedSelf);
                }
                field.SetValue(boxedSelf, deserializeMethod.Invoke(boxedSelf, parameters));
            }
            else if (field.FieldType.IsEnum)
            {
                field.SetValue(boxedSelf, DeserializeEnum(field.FieldType, valueStr, (Enum)field.GetValue(boxedSelf)!));
            }
        }

        public static string DeserializeString(string str)
        {
            return str;
        }

        public static bool DeserializeBool(string str, bool defaultValue)
        {
            if (bool.TryParse(str, out bool result)) { return result; }
            return defaultValue;
        }

        public static float DeserializeFloat(string str, float defaultValue)
        {
            if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)) { return result; }
            return defaultValue;
        }

        public static Int32 DeserializeInt32(string str, Int32 defaultValue)
        {
            if (Int32.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out Int32 result)) { return result; }
            return defaultValue;
        }

        public static Identifier DeserializeIdentifier(string str)
        {
            return str.ToIdentifier();
        }

        public static LanguageIdentifier DeserializeLanguageIdentifier(string str)
        {
            return str.ToLanguageIdentifier();
        }

        public static Color DeserializeColor(string str)
        {
            return XMLExtensions.ParseColor(str);
        }

        public static Enum DeserializeEnum(Type enumType, string str, Enum defaultValue)
        {
            if (Enum.TryParse(enumType, str, out object? result)) { return (Enum)result!; }
            return defaultValue;
        }
        
        public static void SerializeElement<T>(this ref T self, XElement element) where T : struct
        {
            var fields = self.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).ToArray();

            foreach (var field in fields)
            {
                if (field.ShouldSkip()) { continue; }
                self.TrySerialize(field, element);
            }
        }

        public static void TrySerialize<T>(this T self, FieldInfo field, XElement element) where T : struct
        {
            string fieldName = field.Name.ToLowerInvariant();
            object? fieldValue = field.GetValue(self);

            string valueStr = fieldValue?.ToString() ?? "";

            var handler = field.ExtractHandler();

            if (handler != null)
            {
                valueStr = handler.Write(valueStr) ?? "";
            }
            else if (serializeMethods.TryGetValue(field.FieldType, out MethodInfo? method))
            {
                object?[] parameters = { fieldValue };
                valueStr = (string)method.Invoke(self, parameters)!;
            }
            
            element.SetAttributeValue(fieldName, valueStr);
        }

        public static string SerializeBool(bool val)
            => val ? "true" : "false";

        public static string SerializeInt32(Int32 val)
            => val.ToString(CultureInfo.InvariantCulture);
        
        public static string SerializeFloat(float val)
            => val.ToString(CultureInfo.InvariantCulture);

        public static string SerializeColor(Color val)
            => val.ToStringHex();
    }
}