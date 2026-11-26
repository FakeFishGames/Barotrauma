#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Barotrauma.Plugins
{
    // Do not call these methods from vanilla assemblies. They will throw an exception.
    public static class ExtraFieldExtensions
    {
        public static ExtraFields GetExtraFields(object obj, Assembly callingAssembly)
        {
            ArgumentNullException.ThrowIfNull(obj);

            PluginData? matchingPluginData = PluginLoader.TryFindPluginDataForAssembly(callingAssembly);

            if (matchingPluginData == null)
            {
                throw new InvalidOperationException($"Extra fields can only be used by plugins. The assembly '{callingAssembly.GetName()}' is not a registered plugin assembly.");
            }

            return matchingPluginData.StoredFields.GetOrCreateValue(obj);
        }

        public static ExtraFields GetExtraFields(this object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return GetExtraFields(obj, Assembly.GetCallingAssembly());
        }

        public static T? GetExtraField<T>(this object obj, string key)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return GetExtraFields(obj, Assembly.GetCallingAssembly()).Get<T>(key);
        }

        public static bool TryGetExtraField<T>(this object obj, string key, [NotNullWhen(true)] out T? value)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return GetExtraFields(obj, Assembly.GetCallingAssembly()).TryGet(key, out value);
        }

        public static T GetOrAddExtraField<T>(this object obj, string key, T defaultValue)
        {
            ArgumentNullException.ThrowIfNull(obj);
            var extraFields = GetExtraFields(obj, Assembly.GetCallingAssembly());

            var extraField = extraFields.Get<T>(key);
            if (extraField == null)
            {
                extraFields.Set(key, defaultValue);
                return defaultValue;
            }
            return extraField;
        }

        public static void SetExtraField<T>(this object obj, string key, T value)
        {
            ArgumentNullException.ThrowIfNull(obj);
            GetExtraFields(obj, Assembly.GetCallingAssembly()).Set(key, value);
        }
    }

    /// <summary>
    /// A class that allows you to store extra arbitrary fields on an object.
    /// Mainly used for plugins to easily store extra fields on objects.
    /// </summary>
    public sealed class ExtraFields
    {
        private readonly Dictionary<string, object?> fields = new();

        public void Set<T>(string key, T value)
            => fields[key] = value;

        public T? Get<T>(string key)
            => fields.TryGetValue(key, out object? value)
                   ? value switch
                   {
                       null => default,
                       T t  => t,
                       _    => throw new Exception($"Field {key} is not of type {typeof(T)}")
                   }
                   : default;

        public bool TryGet<T>(string key, [NotNullWhen(true)] out T? value)
        {
            if (fields.TryGetValue(key, out object? objValue))
            {
                if (objValue is T t)
                {
                    value = t;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}