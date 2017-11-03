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
    public class Editable : System.Attribute
    {
        public int MaxLength;

        public Editable(int maxLength = 20)
        {
            MaxLength = maxLength;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class InGameEditable : Editable
    {
    }


    [AttributeUsage(AttributeTargets.Property)]
    public class SerializableProperty : System.Attribute
    {
        public object defaultValue;
        public bool isSaveable;

        public SerializableProperty(object defaultValue, bool isSaveable)
        {
            this.defaultValue = defaultValue;
            this.isSaveable = isSaveable;
        }
    }

    class ObjectProperty
    {
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

        public ObjectProperty(PropertyDescriptor property, object obj)
        {
            this.propertyDescriptor = property;
            propertyInfo = property.ComponentType.GetProperty(property.Name);
            this.obj = obj;
        }

        public bool TrySetValue(string value)
        {
            if (value == null) return false;

            if (propertyDescriptor.PropertyType == typeof(string))
            {
                propertyInfo.SetValue(obj, value, null);
            }
            else if (propertyDescriptor.PropertyType == typeof(float))
            {
                float floatVal;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatVal))
                {
                    propertyInfo.SetValue(obj, floatVal, null);
                }
            }
            else if (propertyDescriptor.PropertyType == typeof(bool))
            {
                propertyInfo.SetValue(obj, value.ToLowerInvariant() == "true", null);
            }
            else if (propertyDescriptor.PropertyType == typeof(int))
            {
                int intVal;
                if (int.TryParse(value, out intVal))
                {
                    propertyInfo.SetValue(obj, intVal, null);
                }
            }
            else if (propertyDescriptor.PropertyType == typeof(Vector2))
            {
                propertyInfo.SetValue(obj, XMLExtensions.ParseToVector2(value));
            }
            else if (propertyDescriptor.PropertyType == typeof(Vector3))
            {
                propertyInfo.SetValue(obj, XMLExtensions.ParseToVector3(value));
            }
            else if (propertyDescriptor.PropertyType == typeof(Vector4))
            {
                propertyInfo.SetValue(obj, XMLExtensions.ParseToVector4(value));
            }
            else if (propertyDescriptor.PropertyType == typeof(Color))
            {
                propertyInfo.SetValue(obj, XMLExtensions.ParseToColor(value));
            }
            else if (propertyDescriptor.PropertyType.IsEnum)
            {
                object enumVal;
                try
                {
                    enumVal = Enum.Parse(propertyInfo.PropertyType, value);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj + "\" to " + value + " (not a valid " + propertyInfo.PropertyType + ")", e);
                    return false;
                }
                propertyInfo.SetValue(obj, enumVal);
            }
            else
            {
                DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj + "\" to " + value);
                DebugConsole.ThrowError("(Type not implemented)");

                return false;
            }

            return true;
        }

        public bool TrySetValue(object value)
        {
            if (value == null) return false;
            if (obj == null || propertyDescriptor == null) return false;
            try
            {
                if (value.GetType() == typeof(string) &&
                    propertyDescriptor.PropertyType == typeof(Vector2))
                {
                    propertyInfo.SetValue(obj, XMLExtensions.ParseToVector2(value.ToString()));
                }
                else if (value.GetType() == typeof(string) &&
                    propertyDescriptor.PropertyType == typeof(Vector3))
                {
                    propertyInfo.SetValue(obj, XMLExtensions.ParseToVector3(value.ToString()));
                }
                else if (value.GetType() == typeof(string) &&
                    propertyDescriptor.PropertyType == typeof(Vector4))
                {
                    propertyInfo.SetValue(obj, XMLExtensions.ParseToVector4(value.ToString()));
                }
                else if (value.GetType() == typeof(string) &&
                    propertyDescriptor.PropertyType == typeof(Color))
                {
                    propertyInfo.SetValue(obj, XMLExtensions.ParseToColor(value.ToString()));
                }
                else if (propertyDescriptor.PropertyType != value.GetType())
                {
                    DebugConsole.ThrowError("Failed to set the value of the property \"" + Name + "\" of \"" + obj.ToString() + "\" to " + value.ToString());
                    DebugConsole.ThrowError("(Non-matching type, should be " + propertyDescriptor.PropertyType + " instead of " + value.GetType() + ")");
                    return false;
                }

                propertyInfo.SetValue(obj, value, null);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool TrySetValue(float value)
        {
            try
            {
                propertyInfo.SetValue(obj, value, null);
            }
            catch
            {
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
            catch
            {
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
            catch
            {
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
            catch
            {
                return false;
            }
        }
        
        public static List<ObjectProperty> GetProperties<T>(IPropertyObject obj)
        {
            List<ObjectProperty> editableProperties = new List<ObjectProperty>();

            foreach (var property in obj.ObjectProperties.Values)
            {
                if (property.Attributes.OfType<T>().Any()) editableProperties.Add(property);
            }

            return editableProperties;
        }

        public static Dictionary<string, ObjectProperty> GetProperties(IPropertyObject obj)
        {
            var properties = TypeDescriptor.GetProperties(obj.GetType()).Cast<PropertyDescriptor>();

            Dictionary<string, ObjectProperty> dictionary = new Dictionary<string, ObjectProperty>();

            foreach (var property in properties)
            {
                dictionary.Add(property.Name.ToLowerInvariant(), new ObjectProperty(property, obj));
            }

            return dictionary;
        }

        /*/// <summary>
        /// Sets all serializable properties to their default value
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static Dictionary<string, ObjectProperty> InitProperties(IPropertyObject obj)
        {
            return DeserializeProperties(obj, null);
        }*/

        public static Dictionary<string, ObjectProperty> DeserializeProperties(IPropertyObject obj, XElement element)
        {
            var properties = TypeDescriptor.GetProperties(obj.GetType()).Cast<PropertyDescriptor>();

            Dictionary<string, ObjectProperty> dictionary = new Dictionary<string, ObjectProperty>();

            foreach (var property in properties)
            {
                ObjectProperty objProperty = new ObjectProperty(property, obj);
                dictionary.Add(property.Name.ToLowerInvariant(), objProperty);

                //set the value of the property to the default value if there is one
                foreach (var ini in property.Attributes.OfType<SerializableProperty>())
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
                    ObjectProperty property = null;
                    if (!dictionary.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out property)) continue;
                    if (!property.Attributes.OfType<SerializableProperty>().Any()) continue;
                    property.TrySetValue(attribute.Value);
                }
            }

            return dictionary;
        }

        public static void SerializeProperties(IPropertyObject obj, XElement element, bool saveIfDefault = false)
        {
            var saveProperties = GetProperties<SerializableProperty>(obj);
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
                    foreach (var attribute in property.Attributes.OfType<SerializableProperty>())
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
                if (value.GetType() == typeof(float))
                {
                    //make sure the decimal point isn't converted to a comma or anything else
                    stringValue = ((float)value).ToString("G", CultureInfo.InvariantCulture);
                }
                else if (value.GetType() == typeof(Vector2))
                {
                    Vector2 vector = (Vector2)value;
                    stringValue =
                        vector.X.ToString("G", CultureInfo.InvariantCulture) + "," +
                        vector.Y.ToString("G", CultureInfo.InvariantCulture);
                }
                else if (value.GetType() == typeof(Vector3))
                {
                    Vector3 vector = (Vector3)value;
                    stringValue =
                        vector.X.ToString("G", CultureInfo.InvariantCulture) + "," +
                        vector.Y.ToString("G", CultureInfo.InvariantCulture) + "," +
                        vector.Z.ToString("G", CultureInfo.InvariantCulture);
                }
                else if (value.GetType() == typeof(Vector4))
                {
                    Vector4 vector = (Vector4)value;
                    stringValue =
                        vector.X.ToString("G", CultureInfo.InvariantCulture) + "," +
                        vector.Y.ToString("G", CultureInfo.InvariantCulture) + "," +
                        vector.Z.ToString("G", CultureInfo.InvariantCulture) + "," +
                        vector.W.ToString("G", CultureInfo.InvariantCulture);
                }
                else if (value.GetType() == typeof(Color))
                {
                    Color color = (Color)value;
                    stringValue =
                        (color.R / 255.0f).ToString("G", CultureInfo.InvariantCulture) + "," +
                        (color.G / 255.0f).ToString("G", CultureInfo.InvariantCulture) + "," +
                        (color.B / 255.0f).ToString("G", CultureInfo.InvariantCulture) + "," +
                        (color.A / 255.0f).ToString("G", CultureInfo.InvariantCulture);
                }
                else
                {
                    stringValue = value.ToString();
                }

                element.Add(new XAttribute(property.Name.ToLowerInvariant(), stringValue));
            }
        }
    }
}
