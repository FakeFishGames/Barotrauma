#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma
{
    internal partial class CampaignMetadata
    {
        public CampaignMode Campaign { get; }

        private readonly Dictionary<string, object> data = new Dictionary<string, object>();

        public CampaignMetadata(CampaignMode campaign)
        {
            Campaign = campaign;
        }

        public CampaignMetadata(CampaignMode campaign, XElement element)
        {
            Campaign = campaign;

            foreach (XElement subElement in element.Elements())
            {
                if (string.Equals(subElement.Name.ToString(), "data", StringComparison.InvariantCultureIgnoreCase))
                {
                    string identifier = subElement.GetAttributeString("key", string.Empty).ToLowerInvariant();
                    string value = subElement.GetAttributeString("value", string.Empty);
                    string valueType = subElement.GetAttributeString("type", string.Empty);

                    if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(valueType))
                    {
                        DebugConsole.ThrowError("Unable to load value because one or more of the required attributes are empty.\n" +
                                                $"key: \"{identifier}\", value: \"{value}\", type: \"{valueType}\"");
                        continue;
                    }

                    Type? type = Type.GetType(valueType);

                    if (type == null)
                    {
                        DebugConsole.ThrowError($"Type for {identifier} not found ({valueType}).");
                        continue;
                    }

                    if (type == typeof(float))
                    {
                        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                        {
                            DebugConsole.ThrowError($"Error in campaign metadata: could not parse \"{value}\" as a float.");
                            continue;
                        }
                        data.Add(identifier, floatValue);
                    }
                    else
                    {
                        data.Add(identifier, Convert.ChangeType(value, type));
                    }
                }
            }
        }

        public void SetValue(string identifier, object value)
        {
            identifier = identifier.ToLowerInvariant();

            DebugConsole.Log($"Set the value \"{identifier}\" to {value}");

            if (!data.ContainsKey(identifier))
            {
                data.Add(identifier, value);
                return;
            }

            data[identifier] = value;
        }

        public float GetFloat(string identifier, float? defaultValue = null)
        {
            return (float)GetTypeOrDefault(identifier, typeof(float), defaultValue ?? 0f);
        }

        public int GetInt(string identifier, int? defaultValue = null)
        {
            return (int)GetTypeOrDefault(identifier, typeof(int), defaultValue ?? 0);
        }

        public bool GetBoolean(string identifier, bool? defaultValue = null)
        {
            return (bool)GetTypeOrDefault(identifier, typeof(bool), defaultValue ?? false);
        }

        public string GetString(string identifier, string? defaultValue = null)
        {
            return (string)GetTypeOrDefault(identifier, typeof(string), defaultValue ?? string.Empty);
        }

        public bool HasKey(string identifier)
        {
            identifier = identifier.ToLowerInvariant();
            return data.ContainsKey(identifier);
        }

        private object GetTypeOrDefault(string identifier, Type type, object defaultValue)
        {
            object? value = GetValue(identifier);

            if (value == null)
            {
                SetValue(identifier, defaultValue);
            }
            else if (value.GetType() == type)
            {
                return value;
            }
            else
            {
                DebugConsole.ThrowError($"Attempted to get value \"{identifier}\" as a {type} but the value is {value.GetType()}.");
            }

            return defaultValue;
        }

        public object? GetValue(string identifier)
        {
            return data.ContainsKey(identifier) ? data[identifier] : null;
        }

        public void Save(XElement modeElement)
        {
            XElement element = new XElement("Metadata");

            foreach (var (key, value) in data)
            {
                string valueStr = value?.ToString() ?? "";
                if (value?.GetType() == typeof(float))
                {
                    valueStr = ((float)value).ToString("G", CultureInfo.InvariantCulture);
                }

                element.Add(new XElement("Data",
                    new XAttribute("key", key),
                    new XAttribute("value", valueStr),
                    new XAttribute("type", value?.GetType())));
            }
#if DEBUG || UNSTABLE
            DebugConsole.Log(element.ToString());
#endif
            modeElement.Add(element);
        }
    }
}