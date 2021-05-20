using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    [AttributeUsage(AttributeTargets.Property)]
    class Editable : Attribute
    {
        public int MaxLength;
        public int DecimalCount = 1;

        public int MinValueInt = int.MinValue, MaxValueInt = int.MaxValue;
        public float MinValueFloat = float.MinValue, MaxValueFloat = float.MaxValue;
        public float ValueStep;

        /// <summary>
        /// Labels of the components of a vector property (defaults to x,y,z,w)
        /// </summary>
        public string[] VectorComponentLabels;

        /// <summary>
        /// If a translation can't be found for the property name, this tag is used instead
        /// </summary>
        public string FallBackTextTag;

        /// <summary>
        /// Currently implemented only for int and bool fields. TODO: implement the remaining types (SerializableEntityEditor)
        /// </summary>
        public bool ReadOnly;

        public Editable(int maxLength = 20)
        {
            MaxLength = maxLength;
        }

        public Editable(int minValue, int maxValue)
        {
            MinValueInt = minValue;
            MaxValueInt = maxValue;
        }

        public Editable(float minValue, float maxValue, int decimals = 1)
        {
            MinValueFloat = minValue;
            MaxValueFloat = maxValue;
            DecimalCount = decimals;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    class InGameEditable : Editable
    {
    }

    [AttributeUsage(AttributeTargets.Property)]
    class ConditionallyEditable : Editable
    {
        public ConditionallyEditable(ConditionType conditionType)
        {
            this.conditionType = conditionType;
        }

        private readonly ConditionType conditionType;

        public enum ConditionType
        {
            //These need to exist at compile time, so it is a little awkward
            //I would love to see a better way to do this
            AllowLinkingWifiToChat,
            IsSwappableItem
        }

        public bool IsEditable(ISerializableEntity entity)
        {
            switch (conditionType)
            {
                case ConditionType.AllowLinkingWifiToChat:
                    return GameMain.NetworkMember?.ServerSettings?.AllowLinkingWifiToChat ?? true;
                case ConditionType.IsSwappableItem:
                    return entity is Item item && item.Prefab.SwappableItem != null;
            }
            return false;
        }
    }


    [AttributeUsage(AttributeTargets.Property)]
    public class Serialize : Attribute
    {
        public object defaultValue;
        public bool isSaveable;
        public string translationTextTag;

        /// <summary>
        /// If set to true, the instance values saved in a submarine file will always override the prefab values, even if using a mod that normally overrides instance values.
        /// </summary>
        public bool AlwaysUseInstanceValues;

        public string Description;

        /// <summary>
        /// Makes the property serializable to/from XML
        /// </summary>
        /// <param name="defaultValue">The property is set to this value during deserialization if the value is not defined in XML.</param>
        /// <param name="isSaveable">Is the value saved to XML when serializing.</param>
        /// <param name="translationTextTag">If set to anything else than null, SerializableEntityEditors will show what the text gets translated to or warn if the text is not found in the language files.
        /// <param name="alwaysUseInstanceValues">If set to true, the instance values saved in a submarine file will always override the prefab values, even if using a mod that normally overrides instance values.
        /// Setting the value to a non-empty string will let the user select the text from one whose tag starts with the given string (e.g. RoomName. would show all texts with a RoomName.* tag)</param>
        public Serialize(object defaultValue, bool isSaveable, string description = "", string translationTextTag = null, bool alwaysUseInstanceValues = false)
        {
            this.defaultValue = defaultValue;
            this.isSaveable = isSaveable;
            this.translationTextTag = translationTextTag;
            Description = description;
            AlwaysUseInstanceValues = alwaysUseInstanceValues;
        }
    }

    class SerializableProperty
    {
        private static Dictionary<Type, string> supportedTypes = new Dictionary<Type, string>
        {
            { typeof(bool), "bool" },
            { typeof(int), "int" },
            { typeof(float), "float" },
            { typeof(string), "string" },
            { typeof(Point), "point" },
            { typeof(Vector2), "vector2" },
            { typeof(Vector3), "vector3" },
            { typeof(Vector4), "vector4" },
            { typeof(Rectangle), "rectangle" },
            { typeof(Color), "color" },
        };

        private static readonly Dictionary<Type, Dictionary<string, SerializableProperty>> cachedProperties = 
            new Dictionary<Type, Dictionary<string, SerializableProperty>>();
        public readonly string Name;
        public readonly string NameToLowerInvariant;
        public readonly AttributeCollection Attributes;
        public readonly Type PropertyType;

        public readonly bool OverridePrefabValues;

        public PropertyInfo PropertyInfo { get; private set; }

        public SerializableProperty(PropertyDescriptor property)
        {
            Name = property.Name;
            NameToLowerInvariant = Name.ToLowerInvariant();
            PropertyInfo = property.ComponentType.GetProperty(property.Name);
            PropertyType = property.PropertyType;
            Attributes = property.Attributes;
            OverridePrefabValues = GetAttribute<Serialize>()?.AlwaysUseInstanceValues ?? false;
        }

        public T GetAttribute<T>() where T : Attribute
        {
            foreach (Attribute a in Attributes)
            {
                if (a is T) return (T)a;
            }

            return default;
        }

        public void SetValue(object parentObject, object val)
        {
            PropertyInfo.SetValue(parentObject, val);
        }

        public bool TrySetValue(object parentObject, string value)
        {
            if (value == null) { return false; }

            if (!supportedTypes.TryGetValue(PropertyType, out string typeName))
            {
                if (PropertyType.IsEnum)
                {
                    object enumVal;
                    try
                    {
                        enumVal = Enum.Parse(PropertyInfo.PropertyType, value, true);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject + "\" to " + value + " (not a valid " + PropertyInfo.PropertyType + ")", e);
                        return false;
                    }
                    try
                    {
                        PropertyInfo.SetValue(parentObject, enumVal);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject.ToString() + "\" to " + value.ToString(), e);
                        return false;
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject + "\" to " + value);
                    DebugConsole.ThrowError("(Type not supported)");

                    return false;
                }
            }

            try
            {
                switch (typeName)
                {
                    case "bool":
                        bool boolValue = value == "true" || value == "True";
                        if (TrySetValueWithoutReflection(parentObject, boolValue)) { return true; }
                        PropertyInfo.SetValue(parentObject, boolValue, null);
                        break;
                    case "int":
                        if (int.TryParse(value, out int intVal))
                        {
                            if (TrySetValueWithoutReflection(parentObject, intVal)) { return true; }
                            PropertyInfo.SetValue(parentObject, intVal, null);
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    case "float":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
                        {
                            if (TrySetValueWithoutReflection(parentObject, floatVal)) { return true; }
                            PropertyInfo.SetValue(parentObject, floatVal, null);
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    case "string":
                        PropertyInfo.SetValue(parentObject, value, null);
                        break;
                    case "point":
                        PropertyInfo.SetValue(parentObject, XMLExtensions.ParsePoint(value));
                        break;
                    case "vector2":
                        PropertyInfo.SetValue(parentObject, XMLExtensions.ParseVector2(value));
                        break;
                    case "vector3":
                        PropertyInfo.SetValue(parentObject, XMLExtensions.ParseVector3(value));
                        break;
                    case "vector4":
                        PropertyInfo.SetValue(parentObject, XMLExtensions.ParseVector4(value));
                        break;
                    case "color":
                        PropertyInfo.SetValue(parentObject, XMLExtensions.ParseColor(value));
                        break;
                    case "rectangle":
                        PropertyInfo.SetValue(parentObject, XMLExtensions.ParseRect(value, true));
                        break;
                }
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject.ToString() + "\" to " + value.ToString(), e);
                return false;
            }


            return true;
        }

        public bool TrySetValue(object parentObject, object value)
        {
            if (value == null || parentObject == null || PropertyInfo == null) return false;

            try
            {
                if (!supportedTypes.TryGetValue(PropertyType, out string typeName))
                {
                    if (PropertyType.IsEnum)
                    {
                        object enumVal;
                        try
                        {
                            enumVal = Enum.Parse(PropertyInfo.PropertyType, value.ToString(), true);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject + "\" to " + value + " (not a valid " + PropertyInfo.PropertyType + ")", e);
                            return false;
                        }
                        PropertyInfo.SetValue(parentObject, enumVal);
                        return true;
                    }
                    else
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject + "\" to " + value);
                        DebugConsole.ThrowError("(Type not supported)");

                        return false;
                    }
                }

                try
                {
                    if (value.GetType() == typeof(string))
                    {
                        switch (typeName)
                        {
                            case "string":
                                PropertyInfo.SetValue(parentObject, value, null);
                                return true;
                            case "point":
                                PropertyInfo.SetValue(parentObject, XMLExtensions.ParsePoint((string)value));
                                return true;
                            case "vector2":
                                PropertyInfo.SetValue(parentObject, XMLExtensions.ParseVector2((string)value));
                                return true;
                            case "vector3":
                                PropertyInfo.SetValue(parentObject, XMLExtensions.ParseVector3((string)value));
                                return true;
                            case "vector4":
                                PropertyInfo.SetValue(parentObject, XMLExtensions.ParseVector4((string)value));
                                return true;
                            case "color":
                                PropertyInfo.SetValue(parentObject, XMLExtensions.ParseColor((string)value));
                                return true;
                            case "rectangle":
                                PropertyInfo.SetValue(parentObject, XMLExtensions.ParseRect((string)value, false));
                                return true;
                            default:
                                DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject.ToString() + "\" to " + value.ToString());
                                DebugConsole.ThrowError("(Cannot convert a string to a " + PropertyType.ToString() + ")");
                                return false;
                        }
                    }
                    else if (PropertyType != value.GetType())
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject.ToString() + "\" to " + value.ToString());
                        DebugConsole.ThrowError("(Non-matching type, should be " + PropertyType + " instead of " + value.GetType() + ")");
                        return false;
                    }

                    PropertyInfo.SetValue(parentObject, value, null);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject.ToString() + "\" to " + value.ToString(), e);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.TrySetValue", e);
                return false;
            }
        }

        public bool TrySetValue(object parentObject, float value)
        {
            try
            {
                if (TrySetValueWithoutReflection(parentObject, value)) { return true; }
                PropertyInfo.SetValue(parentObject, value, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.TrySetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.TrySetValue", e);
                return false;
            }

            return true;
        }

        public bool TrySetValue(object parentObject, bool value)
        {
            try
            {
                if (TrySetValueWithoutReflection(parentObject, value)) { return true; }
                PropertyInfo.SetValue(parentObject, value, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.TrySetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.TrySetValue", e);
                return false;
            }
            return true;
        }

        public bool TrySetValue(object parentObject, int value)
        {
            try
            {
                PropertyInfo.SetValue(parentObject, value, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.TrySetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.TrySetValue", e);
                return false;
            }
            return true;
        }

        public object GetValue(object parentObject)
        {
            if (parentObject == null || PropertyInfo == null) { return false; }

            var value = TryGetValueWithoutReflection(parentObject);
            if (value != null) { return value; }
            
            try
            {
                return PropertyInfo.GetValue(parentObject, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.GetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.GetValue", e);
                return false;
            }
        }

        public static string GetSupportedTypeName(Type type)
        {
            if (type.IsEnum) return "Enum";
            if (!supportedTypes.TryGetValue(type, out string typeName))
            {
                return null;
            }
            return typeName;
        }

        /// <summary>
        /// Try getting the values of some commonly used properties directly without reflection
        /// </summary>
        private object TryGetValueWithoutReflection(object parentObject)
        {
            switch (Name)
            {
                case "Voltage":
                    if (parentObject is Powered powered) { return powered.Voltage; }
                    break;
                case "Charge":
                    if (parentObject is PowerContainer powerContainer) { return powerContainer.Charge; }
                    break;
                case "Overload":
                    if (parentObject is PowerTransfer powerTransfer) { return powerTransfer.Overload; }
                    break;
                case "AvailableFuel":
                    { if (parentObject is Reactor reactor) { return reactor.AvailableFuel; } }
                    break;
                case "FissionRate":
                    { if (parentObject is Reactor reactor) { return reactor.FissionRate; } }
                    break;
                case "OxygenFlow":
                    if (parentObject is Vent vent) { return vent.OxygenFlow; }
                    break;
                case "CurrFlow":
                    if (parentObject is Pump pump) { return pump.CurrFlow; }
                    if (parentObject is OxygenGenerator oxygenGenerator) { return oxygenGenerator.CurrFlow; }
                    break;
                case "CurrentVolume":
                    if (parentObject is Engine engine) { return engine.CurrentVolume; }
                    break;
                case "MotionDetected":
                    if (parentObject is MotionSensor motionSensor) { return motionSensor.MotionDetected; }
                    break;
                case "Oxygen":
                    { if (parentObject is Character character) { return character.Oxygen; } }
                    break;
                case "Health":
                    {  if (parentObject is Character character) { return character.Health; } }
                    break;
                case "OxygenAvailable":
                    { if (parentObject is Character character) { return character.OxygenAvailable; } }
                    break;
                case "PressureProtection":
                    { if (parentObject is Character character) { return character.PressureProtection; } }
                    break;
                case "IsDead":
                    { if (parentObject is Character character) { return character.IsDead; } }
                    break;
                case "IsHuman":
                    { if (parentObject is Character character) { return character.IsHuman; } }
                    break;
                case "IsOn":
                    { if (parentObject is LightComponent lightComponent) { return lightComponent.IsOn; } }
                    break;
                case "Condition":
                    {
                        if (parentObject is Item item) { return item.Condition; }
                    }
                    break;
                case "ContainerIdentifier":
                    {
                        if (parentObject is Item item) { return item.ContainerIdentifier; }
                    }
                    break;
                case "PhysicsBodyActive":
                    {
                        if (parentObject is Item item) { return item.PhysicsBodyActive; }
                    }
                    break;
            }

            return null;
        }

        /// <summary>
        /// Try setting the values of some commonly used properties directly without reflection
        /// </summary>
        private bool TrySetValueWithoutReflection(object parentObject, object value)
        {
            switch (Name)
            {
                case "Condition":
                    if (parentObject is Item item && value is float) { item.Condition = (float)value; return true; }
                    break;
                case "Voltage":
                    if (parentObject is Powered powered && value is float) { powered.Voltage = (float)value; return true; }
                    break;
                case "Charge":
                    if (parentObject is PowerContainer powerContainer && value is float) { powerContainer.Charge = (float)value; return true; }
                    break;
                case "AvailableFuel":
                    if (parentObject is Reactor reactor && value is float) { reactor.AvailableFuel = (float)value; return true; }
                    break;
                case "Oxygen":
                    { if (parentObject is Character character && value is float) { character.Oxygen = (float)value; return true; } }
                    break;
                case "HideFace":
                    { if (parentObject is Character character && value is bool) { character.HideFace = (bool)value; return true; } }
                    break;
                case "OxygenAvailable":
                    { if (parentObject is Character character && value is float) { character.OxygenAvailable = (float)value; return true; } }
                    break;
                case "ObstructVision":
                    { if (parentObject is Character character && value is bool) { character.ObstructVision = (bool)value; return true; } }
                    break;
                case "PressureProtection":
                    { if (parentObject is Character character && value is float) { character.PressureProtection = (float)value; return true; } }
                    break;
                case "LowPassMultiplier":
                    { if (parentObject is Character character && value is float) { character.LowPassMultiplier = (float)value; return true; } }
                    break;
                case "SpeedMultiplier":
                    { if (parentObject is Character character && value is float) { character.StackSpeedMultiplier((float)value); return true; } }
                    break;
                case "HealthMultiplier":
                    { if (parentObject is Character character && value is float) { character.StackHealthMultiplier((float)value); return true; } }
                    break;
                case "IsOn":
                    { if (parentObject is LightComponent lightComponent && value is bool) { lightComponent.IsOn = (bool)value; return true; } }
                    break;
            }

            return false;
        }

        public static List<SerializableProperty> GetProperties<T>(ISerializableEntity obj)
        {
            List<SerializableProperty> editableProperties = new List<SerializableProperty>();
            foreach (var property in obj.SerializableProperties.Values)
            {
                if (property.Attributes.OfType<T>().Any()) editableProperties.Add(property);
            }

            return editableProperties;
        }

        public static Dictionary<string, SerializableProperty> GetProperties(object obj)
        {
            Type objType = obj.GetType();
            if (cachedProperties.ContainsKey(objType))
            {
                return cachedProperties[objType];
            }

            var properties = TypeDescriptor.GetProperties(obj.GetType()).Cast<PropertyDescriptor>();
            Dictionary<string, SerializableProperty> dictionary = new Dictionary<string, SerializableProperty>();
            foreach (var property in properties)
            {
                var serializableProperty = new SerializableProperty(property);
                dictionary.Add(serializableProperty.NameToLowerInvariant, serializableProperty);
            }

            cachedProperties[objType] = dictionary;

            return dictionary;
        }
        
        public static Dictionary<string, SerializableProperty> DeserializeProperties(object obj, XElement element = null)
        {
            Dictionary<string, SerializableProperty> dictionary = GetProperties(obj);

            foreach (var property in dictionary.Values)
            {
                //set the value of the property to the default value if there is one
                foreach (var ini in property.Attributes.OfType<Serialize>())
                {
                    property.TrySetValue(obj, ini.defaultValue);
                    break;
                }
            }

            if (element != null)
            {
                //go through all the attributes in the xml element 
                //and set the value of the matching property if it is initializable
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (!dictionary.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out SerializableProperty property)) { continue; }
                    if (!property.Attributes.OfType<Serialize>().Any()) { continue; }
                    property.TrySetValue(obj, attribute.Value);
                }
            }

            return dictionary;
        }

        public static void SerializeProperties(ISerializableEntity obj, XElement element, bool saveIfDefault = false)
        {
            var saveProperties = GetProperties<Serialize>(obj);
            foreach (var property in saveProperties)
            {
                object value = property.GetValue(obj);
                if (value == null) continue;

                if (!saveIfDefault)
                {
                    //only save 
                    //  - if the attribute is saveable and it's different from the default value
                    //  - or can be changed in-game or in the editor
                    bool save = false;
                    foreach (var attribute in property.Attributes.OfType<Serialize>())
                    {
                        if ((attribute.isSaveable && !attribute.defaultValue.Equals(value)) ||
                            property.Attributes.OfType<Editable>().Any())
                        {
                            save = true;
                            break;
                        }
                    }

                    if (!save) continue;
                }

                string stringValue;
                if (!supportedTypes.TryGetValue(value.GetType(), out string typeName))
                {
                    if (property.PropertyType.IsEnum)
                    {
                        stringValue = value.ToString();
                    }
                    else
                    {
                        DebugConsole.ThrowError("Failed to serialize the property \"" + property.Name + "\" of \"" + obj + "\" (type " + property.PropertyType + " not supported)");
                        continue;
                    }
                }
                else
                {
                    switch (typeName)
                    {
                        case "float":
                            //make sure the decimal point isn't converted to a comma or anything else
                            stringValue = ((float)value).ToString("G", CultureInfo.InvariantCulture);
                            break;
                        case "point":
                            stringValue = XMLExtensions.PointToString((Point)value);
                            break;
                        case "vector2":
                            stringValue = XMLExtensions.Vector2ToString((Vector2)value);
                            break;
                        case "vector3":
                            stringValue = XMLExtensions.Vector3ToString((Vector3)value);
                            break;
                        case "vector4":
                            stringValue = XMLExtensions.Vector4ToString((Vector4)value);
                            break;
                        case "color":
                            stringValue = XMLExtensions.ColorToString((Color)value);
                            break;
                        case "rectangle":
                            stringValue = XMLExtensions.RectToString((Rectangle)value);
                            break;
                        default:
                            stringValue = value.ToString();
                            break;
                    }
                }

                element.Attribute(property.Name)?.Remove();
                element.SetAttributeValue(property.NameToLowerInvariant, stringValue);
            }
        }

        /// <summary>
        /// Upgrade the properties of an entity saved with an older version of the game. Properties that should be upgraded are defined using "Upgrade" elements in the config file.
        /// for example, <Upgrade gameversion="0.9.2.0" scale="0.5"/> would force the scale of the entity to 0.5 if it was saved with a version prior to 0.9.2.0.
        /// </summary>
        /// <param name="entity">The entity to upgrade</param>
        /// <param name="configElement">The XML element to get the upgrade instructions from (e.g. the config of an item prefab)</param>
        /// <param name="savedVersion">The game version the entity was saved with</param>
        public static void UpgradeGameVersion(ISerializableEntity entity, XElement configElement, Version savedVersion)
        {
            foreach (XElement subElement in configElement.Elements())
            {
                if (!subElement.Name.ToString().Equals("upgrade", StringComparison.OrdinalIgnoreCase)) { continue; }
                var upgradeVersion = new Version(subElement.GetAttributeString("gameversion", "0.0.0.0"));
                if (savedVersion >= upgradeVersion) { continue; }

                foreach (XAttribute attribute in subElement.Attributes())
                {
                    string attributeName = attribute.Name.ToString().ToLowerInvariant();
                    if (attributeName == "gameversion") { continue; }

                    if (attributeName == "refreshrect")
                    {
                        if (entity is Structure structure)
                        {
                            if (!structure.ResizeHorizontal)
                            {
                                structure.Rect = structure.DefaultRect = new Rectangle(structure.Rect.X, structure.Rect.Y,
                                    (int)structure.Prefab.ScaledSize.X,
                                    structure.Rect.Height);
                            }
                            if (!structure.ResizeVertical)
                            {
                                structure.Rect = structure.DefaultRect = new Rectangle(structure.Rect.X, structure.Rect.Y,
                                    structure.Rect.Width,
                                    (int)structure.Prefab.ScaledSize.Y);
                            }
                        }
                    }

                    if (entity.SerializableProperties.TryGetValue(attributeName, out SerializableProperty property))
                    {
                        FixValue(property, entity, attribute);
                        if (property.Name == nameof(ItemComponent.Msg) && entity is ItemComponent component)
                        {
                            component.ParseMsg();
                        }
                    }
                    else if (entity is Item item1)
                    {
                        foreach (ISerializableEntity component in item1.AllPropertyObjects)
                        {
                            if (component.SerializableProperties.TryGetValue(attributeName, out SerializableProperty componentProperty))
                            {
                                FixValue(componentProperty, component, attribute);
                                if (componentProperty.Name == nameof(ItemComponent.Msg))
                                {
                                    ((ItemComponent)component).ParseMsg();
                                }
                            }
                        }
                    }
                }

                void FixValue(SerializableProperty property, object parentObject, XAttribute attribute)
                {
                    if (attribute.Value.Length > 0 && attribute.Value[0] == '*')
                    {
                        float.TryParse(attribute.Value.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier);

                        if (property.PropertyType == typeof(int))
                        {
                            property.TrySetValue(parentObject, (int)(((int)property.GetValue(parentObject)) * multiplier));
                        }
                        else if (property.PropertyType == typeof(float))
                        {
                            property.TrySetValue(parentObject, (float)property.GetValue(parentObject) * multiplier);
                        }
                        else if (property.PropertyType == typeof(Vector2))
                        {
                            property.TrySetValue(parentObject, (Vector2)property.GetValue(parentObject) * multiplier);
                        }
                        else if (property.PropertyType == typeof(Point))
                        {
                            property.TrySetValue(parentObject, ((Point)property.GetValue(parentObject)).Multiply(multiplier));
                        }
                    }
                    else
                    {
                        property.TrySetValue(parentObject, attribute.Value);
                    }
                }

                if (entity is Item item2)
                {
                    XElement componentElement = subElement.FirstElement();
                    if (componentElement == null) { continue; }
                    ItemComponent itemComponent = item2.Components.First(c => c.Name == componentElement.Name.ToString());
                    if (itemComponent == null) { continue; }
                    foreach (XAttribute attribute in componentElement.Attributes())
                    {
                        string attributeName = attribute.Name.ToString().ToLowerInvariant();
                        if (itemComponent.SerializableProperties.TryGetValue(attributeName, out SerializableProperty property))
                        {
                            FixValue(property, itemComponent, attribute);
                        }
                    }
                    foreach (XElement element in componentElement.Elements())
                    {
                        switch (element.Name.ToString().ToLowerInvariant())
                        {
                            case "requireditem":
                            case "requireditems":
                                itemComponent.requiredItems.Clear();
                                itemComponent.DisabledRequiredItems.Clear();

                                itemComponent.SetRequiredItems(element);
                                break;
                        }
                    }                   
                }
            }
        }
    }
}
