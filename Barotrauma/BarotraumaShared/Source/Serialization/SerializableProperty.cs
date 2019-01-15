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

        public Serialize(object defaultValue, bool isSaveable)
        {
            this.defaultValue = defaultValue;
            this.isSaveable = isSaveable;
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

        private readonly PropertyDescriptor propertyDescriptor;
        private readonly PropertyInfo propertyInfo;
        private readonly object obj;

        public string Name
        {
            get { return propertyDescriptor.Name; }
        }

        public AttributeCollection Attributes
        {
            get { return propertyDescriptor.Attributes; }
        }

        public Type PropertyType
        {
            get
            {
                return propertyInfo.PropertyType;
            }
        }

        public object ParentObject
        {
            get { return obj; }
        }

        public SerializableProperty(PropertyDescriptor property, object obj)
        {
            this.propertyDescriptor = property;
            propertyInfo = property.ComponentType.GetProperty(property.Name);
            this.obj = obj;
        }

        public T GetAttribute<T>() where T : Attribute
        {
            foreach (Attribute a in Attributes)
            {
                if (a is T) return (T)a;
            }

            return default(T);
        }

        public void SetValue(object val)
        {
            propertyInfo.SetValue(obj, val);
        }

        public bool TrySetValue(string value)
        {
            if (value == null) return false;

            string typeName;
            if (!supportedTypes.TryGetValue(propertyDescriptor.PropertyType, out typeName))
            {
                if (propertyDescriptor.PropertyType.IsEnum)
                {
                    object enumVal;
                    try
                    {
                        enumVal = Enum.Parse(propertyInfo.PropertyType, value, true);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj + "\" to " + value + " (not a valid " + propertyInfo.PropertyType + ")", e);
                        return false;
                    }
                    try
                    {
                        propertyInfo.SetValue(obj, enumVal);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj.ToString() + "\" to " + value.ToString(), e);
                        return false;
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj + "\" to " + value);
                    DebugConsole.ThrowError("(Type not supported)");

                    return false;
                }
            }

            try
            {
                switch (typeName)
                {
                    case "bool":
                        propertyInfo.SetValue(obj, value.ToLowerInvariant() == "true", null);
                        break;
                    case "int":
                        int intVal;
                        if (int.TryParse(value, out intVal))
                        {
                            propertyInfo.SetValue(obj, intVal, null);
                        }
                        break;
                    case "float":
                        float floatVal;
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatVal))
                        {
                            propertyInfo.SetValue(obj, floatVal, null);
                        }
                        break;
                    case "string":
                        propertyInfo.SetValue(obj, value, null);
                        break;
                    case "point":
                        propertyInfo.SetValue(obj, XMLExtensions.ParsePoint(value));
                        break;
                    case "vector2":
                        propertyInfo.SetValue(obj, XMLExtensions.ParseVector2(value));
                        break;
                    case "vector3":
                        propertyInfo.SetValue(obj, XMLExtensions.ParseVector3(value));
                        break;
                    case "vector4":
                        propertyInfo.SetValue(obj, XMLExtensions.ParseVector4(value));
                        break;
                    case "color":
                        propertyInfo.SetValue(obj, XMLExtensions.ParseColor(value));
                        break;
                    case "rectangle":
                        propertyInfo.SetValue(obj, XMLExtensions.ParseRect(value, true));
                        break;
                }
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj.ToString() + "\" to " + value.ToString(), e);
                return false;
            }


            return true;
        }

        public bool TrySetValue(object value)
        {
            if (value == null || obj == null || propertyDescriptor == null) return false;

            try
            {
                if (!supportedTypes.TryGetValue(propertyDescriptor.PropertyType, out string typeName))
                {
                    if (propertyDescriptor.PropertyType.IsEnum)
                    {
                        object enumVal;
                        try
                        {
                            enumVal = Enum.Parse(propertyInfo.PropertyType, value.ToString(), true);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj + "\" to " + value + " (not a valid " + propertyInfo.PropertyType + ")", e);
                            return false;
                        }
                        propertyInfo.SetValue(obj, enumVal);
                        return true;
                    }
                    else
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj + "\" to " + value);
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
                                propertyInfo.SetValue(obj, value, null);
                                return true;
                            case "point":
                                propertyInfo.SetValue(obj, XMLExtensions.ParsePoint((string)value));
                                return true;
                            case "vector2":
                                propertyInfo.SetValue(obj, XMLExtensions.ParseVector2((string)value));
                                return true;
                            case "vector3":
                                propertyInfo.SetValue(obj, XMLExtensions.ParseVector3((string)value));
                                return true;
                            case "vector4":
                                propertyInfo.SetValue(obj, XMLExtensions.ParseVector4((string)value));
                                return true;
                            case "color":
                                propertyInfo.SetValue(obj, XMLExtensions.ParseColor((string)value));
                                return true;
                            case "rectangle":
                                propertyInfo.SetValue(obj, XMLExtensions.ParseRect((string)value, false));
                                return true;
                            default:
                                DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj.ToString() + "\" to " + value.ToString());
                                DebugConsole.ThrowError("(Cannot convert a string to a " + propertyDescriptor.PropertyType.ToString() + ")");
                                return false;
                        }
                    }
                    else if (propertyDescriptor.PropertyType != value.GetType())
                    {
                        DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj.ToString() + "\" to " + value.ToString());
                        DebugConsole.ThrowError("(Non-matching type, should be " + propertyDescriptor.PropertyType + " instead of " + value.GetType() + ")");
                        return false;
                    }

                    propertyInfo.SetValue(obj, value, null);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj.ToString() + "\" to " + value.ToString(), e);
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

        public bool TrySetValue(float value)
        {
            try
            {
                propertyInfo.SetValue(obj, value, null);
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

        public bool TrySetValue(bool value)
        {
            try
            {
                propertyInfo.SetValue(obj, value, null);
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

        public bool TrySetValue(int value)
        {
            try
            {
                propertyInfo.SetValue(obj, value, null);
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

        public object GetValue()
        {
            if (obj == null || propertyDescriptor == null) return false;

            try
            {
                return propertyInfo.GetValue(obj, null);
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
            string typeName = null;
            if (type.IsEnum) return "Enum";
            if (!supportedTypes.TryGetValue(type, out typeName))
            {
                return null;
            }
            return typeName;
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

        public static Dictionary<string, SerializableProperty> GetProperties(ISerializableEntity obj)
        {
            var properties = TypeDescriptor.GetProperties(obj.GetType()).Cast<PropertyDescriptor>();

            Dictionary<string, SerializableProperty> dictionary = new Dictionary<string, SerializableProperty>();

            foreach (var property in properties)
            {
                dictionary.Add(property.Name.ToLowerInvariant(), new SerializableProperty(property, obj));
            }

            return dictionary;
        }
        
        public static Dictionary<string, SerializableProperty> DeserializeProperties(object obj, XElement element = null)
        {
            var properties = TypeDescriptor.GetProperties(obj.GetType()).Cast<PropertyDescriptor>();

            Dictionary<string, SerializableProperty> dictionary = new Dictionary<string, SerializableProperty>();

            foreach (var property in properties)
            {
                SerializableProperty objProperty = new SerializableProperty(property, obj);
                dictionary.Add(property.Name.ToLowerInvariant(), objProperty);

                //set the value of the property to the default value if there is one
                foreach (var ini in property.Attributes.OfType<Serialize>())
                {
                    objProperty.TrySetValue(ini.defaultValue);
                    break;
                }
            }

            if (element != null)
            {
                //go through all the attributes in the xml element 
                //and set the value of the matching property if it is initializable
                foreach (XAttribute attribute in element.Attributes())
                {
                    SerializableProperty property = null;
                    if (!dictionary.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out property)) continue;
                    if (!property.Attributes.OfType<Serialize>().Any()) continue;
                    property.TrySetValue(attribute.Value);
                }
            }

            return dictionary;
        }

        public static void SerializeProperties(ISerializableEntity obj, XElement element, bool saveIfDefault = false)
        {
            var saveProperties = GetProperties<Serialize>(obj);
            foreach (var property in saveProperties)
            {
                object value = property.GetValue();
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
                element.SetAttributeValue(property.Name.ToLowerInvariant(), stringValue);
            }
        }
    }
}
