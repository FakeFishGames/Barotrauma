#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

// ReSharper disable ArrangeThisQualifier
namespace Barotrauma
{
    internal sealed class PropertyReference
    {
        public object? OriginalValue { get; private set; }

        public readonly Identifier Name;

        private readonly string Multiplier;

        private static readonly char[] prefixCharacters = { '=', '/', '*', 'x', '-', '+' };

        private readonly Upgrade upgrade;

        private PropertyReference(Identifier name, string multiplier, Upgrade upgrade)
        {
            this.Name = name;
            this.Multiplier = multiplier;
            this.upgrade = upgrade;
        }

        public void SetOriginalValue(object value)
        {
            OriginalValue ??= value;
        }

        /// <summary>
        /// Calculate the new value of the property
        /// </summary>
        /// <param name="level">level of the upgrade</param>
        /// <returns></returns>
        public object CalculateUpgrade(int level)
        {
            switch (OriginalValue)
            {
                case float _:
                case int _:
                case double _:
                {
                    var value = (float) OriginalValue;
                    return level == 0 ? value : CalculateUpgrade(value, level, Multiplier);
                }
                case bool _ when bool.TryParse(Multiplier, out bool result):
                {
                    return result;
                }
                default:
                {
                    DebugConsole.AddWarning($"Original value of \"{Name}\" in the upgrade \"{upgrade.Prefab.Name}\" is not a integer, float, double or boolean but {OriginalValue?.GetType()} with a value of ({OriginalValue}). \n" +
                                            "The value has been assumed to be '0', did you forget a Convert.ChangeType()?");
                    break;
                }
            }

            return 0;
        }

        public static float CalculateUpgrade(float value, int level, string multiplier)
        {
            if (multiplier[^1] != '%')
            {
                return CalculateUpgradeFloat(multiplier, value , level);
            }

            return ApplyPercentage(value, UpgradePrefab.ParsePercentage(multiplier, Identifier.Empty, suppressWarnings: true), level);
        }

        private static float CalculateUpgradeFloat(string multiplier, float value, int level)
        {
            float multiplierFloat = ParseValue(multiplier, value);

            switch (multiplier[0])
            {
                case '*':
                case 'x':
                    return value * (multiplierFloat * level);
                case '/':
                    return value / (multiplierFloat * level);
                case '-':
                    return value - (multiplierFloat * level);
                case '+':
                    return value + (multiplierFloat * level);
                case '=':
                    return multiplierFloat;
            }

            return 0;
        }

        /// <summary>
        /// Sets the OriginalValue to a value stored in the save XML element
        /// </summary>
        /// <param name="savedElement"></param>
        public void ApplySavedValue(XElement? savedElement)
        {
            if (savedElement == null) { return; }

            foreach (var savedValue in savedElement.Elements())
            {
                if (savedValue.NameAsIdentifier() == Name)
                {
                    string value = savedValue.GetAttributeString("value", string.Empty);

                    if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float floatValue))
                    {
                        OriginalValue = floatValue;
                    }
                    else if (bool.TryParse(value, out bool boolValue))
                    {
                        OriginalValue = boolValue;
                    }
                    else
                    {
                        OriginalValue = value;
                    }
                }
            }
        }

        /// <summary>
        /// Recursively apply a percentage to a value certain amount of times
        /// </summary>
        /// <param name="value">original value</param>
        /// <param name="amount">percentage increase/decrease</param>
        /// <param name="times">how many times to apply the percentage change</param>
        /// <returns></returns>
        private static float ApplyPercentage(float value, float amount, int times)
        {
            return (1f + (amount / 100f * times)) * value;
        }

        public static PropertyReference[] ParseAttributes(IEnumerable<XAttribute> attributes, Upgrade upgrade)
        {
            return attributes.Select(attribute => new PropertyReference(attribute.NameAsIdentifier(), attribute.Value, upgrade)).ToArray();
        }

        private static float ParseValue(string multiplier, object? originalValue)
        {
            if (multiplier.Length > 1)
            {
                if (prefixCharacters.Contains(multiplier[0]))
                {
                    if (float.TryParse(multiplier.Substring(1).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out float value)) { return value; }

                    if (originalValue is float || originalValue is int || originalValue is double) { return (float) originalValue; }
                }
            }

            return 1;
        }
    }

    internal sealed class Upgrade : IDisposable
    {
        private ISerializableEntity TargetEntity { get; }

        public Dictionary<ISerializableEntity, PropertyReference[]> TargetComponents { get; }

        public UpgradePrefab Prefab { get; }

        public Identifier Identifier => Prefab.Identifier;

        public int Level { get; set; }

        public bool Disposed { get; private set; }

        private readonly ContentXElement sourceElement;

        public Upgrade(ISerializableEntity targetEntity, UpgradePrefab prefab, int level, XContainer? saveElement = null)
        {
            this.TargetEntity = targetEntity;
            this.sourceElement = prefab.SourceElement;
            this.Prefab = prefab;
            this.Level = level;

            var targetProperties = new Dictionary<ISerializableEntity, PropertyReference[]>();

            List<XElement>? saveElements = saveElement?.Elements().ToList();

            foreach (var subElement in prefab.SourceElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "decorativesprite":
                    case "sprite":
                    case "price":
                        break;
                    case "item":
                    case "structure":
                    case "base":
                    case "root":
                    case "this":
                        XElement? savedRootElement = saveElements?.Find(e => string.Equals(e.Name.ToString(), "This", StringComparison.OrdinalIgnoreCase));

                        var rootProperties = PropertyReference.ParseAttributes(subElement.Attributes(), this);
                        targetProperties.Add(targetEntity, rootProperties);

                        foreach (var propertyRef in rootProperties)
                        {
                            propertyRef.ApplySavedValue(savedRootElement);
                        }

                        break;
                    default:
                    {
                        if (targetEntity is Item item)
                        {
                            ISerializableEntity[]? itemComponents = FindItemComponent(item, subElement.Name.ToString());

                            if (itemComponents != null && itemComponents.Any())
                            {
                                foreach (ISerializableEntity sEntity in itemComponents)
                                {
                                    XElement? savedElement = saveElements?.Find(e => string.Equals(e.Name.ToString(), sEntity.Name, StringComparison.OrdinalIgnoreCase));
                                    PropertyReference[] properties = PropertyReference.ParseAttributes(subElement.Attributes(), this);

                                    foreach (PropertyReference propertyRef in properties)
                                    {
                                        propertyRef.ApplySavedValue(savedElement);
                                    }

                                    targetProperties.Add(sEntity, properties);
                                }
                            }
                        }

                        break;
                    }
                }
            }

            TargetComponents = targetProperties;

            if (saveElement != null)
            {
                ResetNonAffectedProperties(saveElement);
            }
        }

        /// <summary>
        /// Finds saved properties in the XML element and resets properties that are not managed by the upgrade anymore to their default values
        /// </summary>
        /// <param name="saveElement">XML save element</param>
        private void ResetNonAffectedProperties(XContainer saveElement)
        {
            foreach (var element in saveElement.Elements().Elements())
            {
                if (TargetComponents.SelectMany(pair => pair.Value)
                                    .Select(@ref => @ref.Name)
                                    .Any(@identifier => @identifier == element.NameAsIdentifier())) { continue; }

                string value = element.GetAttributeString("value", string.Empty);
                Identifier name = element.NameAsIdentifier();
                XElement parentElement = element.Parent ?? throw new NullReferenceException("Unable to reset properties: Parent element is null.");
                string componentName = parentElement.Name.ToString();

                DebugConsole.AddWarning($"Upgrade \"{Prefab.Name}\" in {TargetEntity.Name} does not affect the property \"{name}\" but the save file suggest it has done so before (has it been overriden?). \n" +
                                        $"The property has been reset to the original value of {value} and will be ignored from now on.");

                if (string.Equals(componentName, "This", StringComparison.OrdinalIgnoreCase))
                {
                    if (TargetEntity.SerializableProperties.TryGetValue(name, out SerializableProperty? property))
                    {
                        property?.SetValue(TargetEntity, Convert.ChangeType(value, property!.GetValue(TargetEntity).GetType(), NumberFormatInfo.InvariantInfo));
                    }
                }
                else if (TargetEntity is Item item)
                {
                    ISerializableEntity[]? foundComponents = FindItemComponent(item, componentName);
                    if (foundComponents == null) { continue; }

                    foreach (var serializableEntity in foundComponents)
                    {
                        if (serializableEntity.SerializableProperties.TryGetValue(name, out SerializableProperty? property))
                        {
                            property?.SetValue(serializableEntity, Convert.ChangeType(value, property!.GetValue(serializableEntity).GetType(), NumberFormatInfo.InvariantInfo));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Find an item component matching the XML element
        /// </summary>
        /// <param name="item">Target item</param>
        /// <param name="name">XML ItemComponent element</param>
        /// <returns>Array of matching ItemComponents or null</returns>
        private static ISerializableEntity[]? FindItemComponent(Item item, string name)
        {
            Type? type = Type.GetType($"Barotrauma.Items.Components.{name.ToLowerInvariant()}", false, true);

            if (type != null)
            {
                int count = item.Components.Count(ic => ic.GetType() == type);
                if (count == 0) { return null; }

                IEnumerable<ItemComponent> itemComponents = item.Components.Where(ic => ic.GetType() == type);
                return itemComponents.Cast<ISerializableEntity>().ToArray();
            }

            return null;
        }

        public void Save(XElement element)
        {
            var upgrade = new XElement("Upgrade", new XAttribute("identifier", Identifier), new XAttribute("level", Level));

            foreach (var targetComponent in TargetComponents)
            {
                var (key, value) = targetComponent;

                string name = key is ItemComponent ? key.Name : "This";

                var subElement = new XElement(name);
                foreach (PropertyReference propertyRef in value)
                {
                    if (propertyRef.OriginalValue != null)
                    {
                        subElement.Add(new XElement(propertyRef.Name.Value,
                            new XAttribute("value", propertyRef.OriginalValue)));
                    }
                    else if (!Prefab.SuppressWarnings)
                    {
                        DebugConsole.AddWarning($"Failed to save upgrade \"{Prefab.Name}\" on {TargetEntity.Name} because property reference \"{propertyRef.Name}\" is missing original values. \n" +
                                                "Upgrades should always call Upgrade.ApplyUpgrade() or manually set the original value in a property reference after they have been added. \n" +
                                                "If you are not a developer submit a bug report at https://github.com/Regalis11/Barotrauma/issues/.");
                    }
                }

                upgrade.Add(subElement);
            }

            element.Add(upgrade);
        }

        /// <summary>
        /// Applies the upgrade to the target item and components
        /// </summary>
        /// <remarks>
        /// This method should be called every time a new upgrade is added unless you set the original values of PropertyReference manually.
        /// Do note that <see cref="MapEntity.AddUpgrade"/> calls this method automatically.
        /// </remarks>
        public void ApplyUpgrade()
        {
            foreach (var keyValuePair in TargetComponents)
            {
                var (entity, properties) = keyValuePair;

                foreach (PropertyReference propertyReference in properties)
                {
                    if (entity.SerializableProperties.TryGetValue(propertyReference.Name, out SerializableProperty? property) && property != null)
                    {
                        object? originalValue = property.GetValue(entity);
                        propertyReference.SetOriginalValue(originalValue);
                        object newValue = Convert.ChangeType(propertyReference.CalculateUpgrade(Level), originalValue.GetType(), NumberFormatInfo.InvariantInfo);
                        property.SetValue(entity, newValue);
                    }
                    else
                    {
                        // Find the closest matching property name and suggest it in the error message
                        string matchingString = string.Empty;
                        int closestMatch = int.MaxValue;
                        foreach (var (propertyName, _) in entity.SerializableProperties)
                        {
                            int match = ToolBox.LevenshteinDistance(propertyName.Value, propertyReference.Name.Value);
                            if (match < closestMatch)
                            {
                                matchingString = propertyName.Value ?? "";
                                closestMatch = match;
                            }
                        }

                        DebugConsole.ThrowError($"The upgrade \"{Prefab.Name}\" cannot be applied to {entity.Name} because it does not contain the property \"{propertyReference.Name}\" and has been ignored. \n" +
                                                $"Did you mean \"{matchingString}\"?");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!Disposed)
            {
                TargetComponents.Clear();
            }

            Disposed = true;
        }
    }
}