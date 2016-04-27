using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Linq;
using System.ComponentModel;


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
    public class HasDefaultValue : System.Attribute
    {
        public object defaultValue;
        public bool isSaveable;

        public HasDefaultValue(object defaultValue, bool isSaveable)
        {
            this.defaultValue = defaultValue;
            this.isSaveable = isSaveable;
        }
    }

    class ObjectProperty
    {
        readonly PropertyDescriptor property;
        readonly PropertyInfo propertyInfo;
        readonly object obj;
        
        public string Name
        {
            get { return property.Name; }
        }

        public AttributeCollection Attributes
        {
            get { return property.Attributes; }
        }

        public ObjectProperty(PropertyDescriptor property, object obj)
        {
            this.property = property;
            propertyInfo = property.ComponentType.GetProperty(property.Name);
            this.obj = obj;
        }
                
        public bool TrySetValue(string value)
        {
            if (value == null) return false;
            
            if (property.PropertyType == typeof(string))
            {
                propertyInfo.SetValue(obj, value, null);
            }
            else if (property.PropertyType == typeof(float))
            {
                float floatVal = 0.0f;

                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatVal))
                {
                    propertyInfo.SetValue(obj, floatVal, null);
                }
            }
            else if (property.PropertyType == typeof(bool))
            {
                propertyInfo.SetValue(obj, (value.ToLowerInvariant() == "true"), null);                
            }
            else if (property.PropertyType == typeof(int))
            {
                int intVal = 0;
                if (int.TryParse(value, out intVal))
                {
                    propertyInfo.SetValue(obj, intVal, null);
                }
            }
            else
            {
                DebugConsole.ThrowError("Failed to set the value of the property ''" + Name + "'' of ''" + obj + "'' to " + value);
                DebugConsole.ThrowError("(Type not implemented)");

                return false;
            }

            return true;
        }

        public bool TrySetValue(object value)
        {
            if (value == null) return false;
                     
            if (property.PropertyType!= value.GetType())
            {
                DebugConsole.ThrowError("Failed to set the value of the property ''"+Name+"'' of ''"+obj.ToString()+"'' to "+value.ToString());

                DebugConsole.ThrowError("(Non-matching type, should be "+property.PropertyType+" instead of " +value.GetType()+")");
                return false;
            }

            if (obj == null || property == null) return false;
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
            if (obj == null || property == null) return false;

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

        public static Dictionary<string, ObjectProperty> InitProperties(IPropertyObject obj)
        {
            return InitProperties(obj, null);
        }

        public static Dictionary<string, ObjectProperty> InitProperties(IPropertyObject obj, XElement element)
        {
            var properties = TypeDescriptor.GetProperties(obj.GetType()).Cast<PropertyDescriptor>();

            Dictionary<string, ObjectProperty> dictionary = new Dictionary<string, ObjectProperty>();

            foreach (var property in properties)
            {
                ObjectProperty objProperty = new ObjectProperty(property, obj);
                dictionary.Add(property.Name.ToLowerInvariant(), objProperty);

                //set the value of the property to the default value if there is one
                foreach (var ini in property.Attributes.OfType<HasDefaultValue>())
                {
                    objProperty.TrySetValue(ini.defaultValue);
                    break;
                }
            }
            
            if (element!=null)
            {
                //go through all the attributes in the xml element 
                //and set the value of the matching property if it is initializable
                foreach (XAttribute attribute in element.Attributes())
                {
                    ObjectProperty property = null;
                    if (!dictionary.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out property)) continue;
                    if (!property.Attributes.OfType<HasDefaultValue>().Any()) continue;
                    property.TrySetValue(attribute.Value);
                }            
            }

            return dictionary;
        }
    
        public static void SaveProperties(IPropertyObject obj, XElement element)
        {
            var saveProperties = GetProperties<HasDefaultValue>(obj);
            foreach (var property in saveProperties)
            {
                object value = property.GetValue();
                if (value == null) continue;
                
                //only save if the value has been changed from the default value and the attribute is saveable
                bool dontSave = true;
                foreach (var attribute in property.Attributes.OfType<HasDefaultValue>())
                {
                    if (attribute.isSaveable && !attribute.defaultValue.Equals(value))
                    {
                        dontSave = false;
                        break;
                    }    
                }

                if (dontSave) continue;

                string stringValue;
                if (value is float)
                {
                    //do this to make sure the decimal point isn't converted to a comma or anything like that
                    stringValue = ((float)value).ToString("G", CultureInfo.InvariantCulture);
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
