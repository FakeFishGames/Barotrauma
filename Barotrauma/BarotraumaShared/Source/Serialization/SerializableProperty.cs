using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;


namespace Barotrauma
{
    [AttributeUsage(AttributeTargets.Property)]
    public class Editable : Attribute
    {
        public int MaxLength;
        public int DecimalCount = 1;

        public int MinValueInt = int.MinValue, MaxValueInt = int.MaxValue;
        public float MinValueFloat = float.MinValue, MaxValueFloat = float.MaxValue;
        public float ValueStep;

        public string ToolTip;

        public string DisplayName;

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
    public class InGameEditable : Editable
    {
    }


    [AttributeUsage(AttributeTargets.Property)]
    public class Serialize : Attribute
    {
        public object defaultValue;
        public bool isSaveable;
        public string translationTextTag;

        /// <summary>
        /// Makes the property serializable to/from XML
        /// </summary>
        /// <param name="defaultValue">The property is set to this value during deserialization if the value is not defined in XML.</param>
        /// <param name="isSaveable">Is the value saved to XML when serializing.</param>
        /// <param name="translationTextTag">If set to anything else than null, SerializableEntityEditors will show what the text gets translated to or warn if the text is not found in the language files.
        /// Setting the value to a non-empty string will let the user select the text from one whose tag starts with the given string (e.g. RoomName. would show all texts with a RoomName.* tag)</param>
        public Serialize(object defaultValue, bool isSaveable, string translationTextTag = null)
        {
            this.defaultValue = defaultValue;
            this.isSaveable = isSaveable;
            this.translationTextTag = translationTextTag;
        }
    }

    public class SerializableProperty
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

        private readonly PropertyInfo propertyInfo;

        public readonly string Name;
        public readonly string NameToLowerInvariant;
        public readonly AttributeCollection Attributes;
        public readonly Type PropertyType;

        public PropertyInfo PropertyInfo
        {
            get { return propertyInfo; }
        }

        public SerializableProperty(PropertyDescriptor property, object obj)
        {
            Name = property.Name;
            NameToLowerInvariant = Name.ToLowerInvariant();
            propertyInfo = property.ComponentType.GetProperty(property.Name);
            PropertyType = property.PropertyType;
            Attributes = property.Attributes;
        }

        public T GetAttribute<T>() where T : Attribute
        {
            foreach (Attribute a in Attributes)
            {
                if (a is T) return (T)a;
            }

            return default(T);
        }

        public void SetValue(object parentObject, object val)
        {
            propertyInfo.SetValue(parentObject, val);
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
                        enumVal = Enum.Parse(propertyInfo.PropertyType, value, true);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject + "\" to " + value + " (not a valid " + propertyInfo.PropertyType + ")", e);
                        return false;
                    }
                    try
                    {
                        propertyInfo.SetValue(parentObject, enumVal);
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
                        propertyInfo.SetValue(parentObject, boolValue, null);
                        break;
                    case "int":
                        int intVal;
                        if (int.TryParse(value, out intVal))
                        {
                            if (TrySetValueWithoutReflection(parentObject, intVal)) { return true; }
                            propertyInfo.SetValue(parentObject, intVal, null);
                        }
                        break;
                    case "float":
                        float floatVal;
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatVal))
                        {
                            if (TrySetValueWithoutReflection(parentObject, floatVal)) { return true; }
                            propertyInfo.SetValue(parentObject, floatVal, null);
                        }
                        break;
                    case "string":
                        propertyInfo.SetValue(parentObject, value, null);
                        break;
                    case "point":
                        propertyInfo.SetValue(parentObject, XMLExtensions.ParsePoint(value));
                        break;
                    case "vector2":
                        propertyInfo.SetValue(parentObject, XMLExtensions.ParseVector2(value));
                        break;
                    case "vector3":
                        propertyInfo.SetValue(parentObject, XMLExtensions.ParseVector3(value));
                        break;
                    case "vector4":
                        propertyInfo.SetValue(parentObject, XMLExtensions.ParseVector4(value));
                        break;
                    case "color":
                        propertyInfo.SetValue(parentObject, XMLExtensions.ParseColor(value));
                        break;
                    case "rectangle":
                        propertyInfo.SetValue(parentObject, XMLExtensions.ParseRect(value, true));
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
            if (value == null || parentObject == null || propertyInfo == null) return false;

            try
            {
                if (!supportedTypes.TryGetValue(PropertyType, out string typeName))
                {
                    if (PropertyType.IsEnum)
                    {
                        object enumVal;
                        try
                        {
                            enumVal = Enum.Parse(propertyInfo.PropertyType, value.ToString(), true);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + parentObject + "\" to " + value + " (not a valid " + propertyInfo.PropertyType + ")", e);
                            return false;
                        }
                        propertyInfo.SetValue(parentObject, enumVal);
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
                                propertyInfo.SetValue(parentObject, value, null);
                                return true;
                            case "point":
                                propertyInfo.SetValue(parentObject, XMLExtensions.ParsePoint((string)value));
                                return true;
                            case "vector2":
                                propertyInfo.SetValue(parentObject, XMLExtensions.ParseVector2((string)value));
                                return true;
                            case "vector3":
                                propertyInfo.SetValue(parentObject, XMLExtensions.ParseVector3((string)value));
                                return true;
                            case "vector4":
                                propertyInfo.SetValue(parentObject, XMLExtensions.ParseVector4((string)value));
                                return true;
                            case "color":
                                propertyInfo.SetValue(parentObject, XMLExtensions.ParseColor((string)value));
                                return true;
                            case "rectangle":
                                propertyInfo.SetValue(parentObject, XMLExtensions.ParseRect((string)value, false));
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

                    propertyInfo.SetValue(parentObject, value, null);
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
                propertyInfo.SetValue(parentObject, value, null);
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
                propertyInfo.SetValue(parentObject, value, null);
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
                propertyInfo.SetValue(parentObject, value, null);
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
            if (parentObject == null || propertyInfo == null) { return false; }

            var value = TryGetValueWithoutReflection(parentObject);
            if (value != null) { return value; }
            
            try
            {
                return propertyInfo.GetValue(parentObject, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.GetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.TrySetValue", e);
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
                case "Condition":
                    if (parentObject is Item item) { return item.Condition; }                    
                    break;
                case "Voltage":
                    if (parentObject is Powered powered) { return powered.Voltage; }
                    break;
                case "Charge":
                    if (parentObject is PowerContainer powerContainer) { return powerContainer.Charge; }
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
                case "IsOn":
                    { if (parentObject is LightComponent lightComponent) { return lightComponent.IsOn; } }
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
                    { if (parentObject is Character character && value is float) { character.SpeedMultiplier = (float)value; return true; } }
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
                var serializableProperty = new SerializableProperty(property, obj);
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
    }
}
