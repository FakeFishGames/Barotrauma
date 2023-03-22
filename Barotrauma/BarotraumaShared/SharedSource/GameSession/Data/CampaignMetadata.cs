#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma
{
    internal partial class CampaignMetadata
    {
        private readonly Dictionary<Identifier, object> data = new Dictionary<Identifier, object>();

        public CampaignMetadata()
        {
        }

        public void Load(XElement element)
        {
            data.Clear();
            foreach (var subElement in element.Elements())
            {
                if (string.Equals(subElement.Name.ToString(), "data", StringComparison.InvariantCultureIgnoreCase))
                {
                    Identifier identifier = subElement.GetAttributeIdentifier("key", Identifier.Empty);
                    string value = subElement.GetAttributeString("value", string.Empty);
                    string valueType = subElement.GetAttributeString("type", string.Empty);

                    if (identifier.IsEmpty || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(valueType))
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
                    else if (type == typeof(Identifier))
                    {
                        data.Add(identifier, value.ToIdentifier());
                    }
                    else
                    {
                        data.Add(identifier, Convert.ChangeType(value, type, NumberFormatInfo.InvariantInfo));
                    }
                }
            }
        }

        public void SetValue(Identifier identifier, object value)
        {
            DebugConsole.Log($"Set the value \"{identifier}\" to {value}");

            SteamAchievementManager.OnCampaignMetadataSet(identifier, value, unlockClients: true);

            if (!data.ContainsKey(identifier))
            {
                data.Add(identifier, value);
                return;
            }

            data[identifier] = value;
        }

        public float GetFloat(Identifier identifier, float? defaultValue = null)
        {
            return (float)GetTypeOrDefault(identifier, typeof(float), defaultValue ?? 0f);
        }

        public int GetInt(Identifier identifier, int? defaultValue = null)
        {
            return (int)GetTypeOrDefault(identifier, typeof(int), defaultValue ?? 0);
        }

        public bool GetBoolean(Identifier identifier, bool? defaultValue = null)
        {
            return (bool)GetTypeOrDefault(identifier, typeof(bool), defaultValue ?? false);
        }

        public string GetString(Identifier identifier, string? defaultValue = null)
        {
            return (string)GetTypeOrDefault(identifier, typeof(string), defaultValue ?? string.Empty);
        }

        public bool HasKey(Identifier identifier)
        {
            return data.ContainsKey(identifier);
        }

        private object GetTypeOrDefault(Identifier identifier, Type type, object defaultValue)
        {
            object? value = GetValue(identifier);
            if (value != null)
            {
                if (value.GetType() == type)
                {
                    return value;
                }
                else
                {
                    DebugConsole.ThrowError($"Attempted to get value \"{identifier}\" as a {type} but the value is {value.GetType()}.");
                }
            }
            return defaultValue;
        }

        public object? GetValue(Identifier identifier)
        {
            return data.ContainsKey(identifier) ? data[identifier] : null;
        }

        public void Save(XElement modeElement)
        {
            XElement element = new XElement("Metadata");

            foreach (var (key, value) in data)
            {
                string valueStr = value.ToString() ?? throw new NullReferenceException();
                if (value is float f)
                {
                    valueStr = f.ToString("G", CultureInfo.InvariantCulture);
                }

                element.Add(new XElement("Data",
                    new XAttribute("key", key),
                    new XAttribute("value", valueStr),
                    new XAttribute("type", value.GetType())));
            }
#if DEBUG
            DebugConsole.Log(element.ToString());
#endif
            modeElement.Add(element);
        }
    }
}