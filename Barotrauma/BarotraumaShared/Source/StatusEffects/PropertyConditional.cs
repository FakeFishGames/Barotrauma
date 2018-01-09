using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class PropertyConditional
    {
        public readonly string Attribute;
        public readonly string Operator;
        public readonly object Value;

        public PropertyConditional(XAttribute attribute)
        {
            string attributeString = attribute.Value.ToString();
            string atStr = attributeString;
            string[] splitString = atStr.Split(' ');
            string op = splitString[0];
            if (splitString.Length > 0)
            {
                for (int i = 1; i < splitString.Length; i++)
                {
                    atStr = splitString[i] + (i > 1 && i < splitString.Length ? " " : "");
                }
            }
            //thanks xml for not letting me use < or > in attributes :(
            switch (op)
            {
                case "e":
                case "eq":
                case "equals":
                    op = "==";
                    break;
                case "ne":
                case "neq":
                case "notequals":
                case "!":
                case "!e":
                case "!eq":
                case "!equals":
                    op = "!=";
                    break;
                case "gt":
                case "greaterthan":
                    op = ">";
                    break;
                case "lt":
                case "lessthan":
                    op = "<";
                    break;
                case "gte":
                case "gteq":
                case "greaterthanequals":
                    op = ">=";
                    break;
                case "lte":
                case "lteq":
                case "lessthanequals":
                    op = "<=";
                    break;
                default:
                    if (op != "==" && op != "!=" && op != ">" && op != "<" && op != ">=" && op != "<=") //Didn't use escape strings or anything
                    {
                        atStr = attributeString; //We probably don't even have an operator
                        op = "==";
                    }
                    break;
            }

            Attribute = attribute.Name.ToString().ToLowerInvariant();
            Operator = op;
            Value = atStr;            
        }

        public PropertyConditional(string Attribute, string Operator, object Value)
        {
            this.Attribute = Attribute;
            this.Operator = Operator;
            this.Value = Value;
        }

        public bool Matches(SerializableProperty property)
        {
            if (property.GetValue() == null)
            {
                DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "- property.GetValue() returns null!!");
                return false;
            }
            Type type = property.GetValue().GetType();

            float? floatValue = null;
            float? floatProperty = null;
            if (type == typeof(float) || type == typeof(int))
            {
                floatValue = Convert.ToSingle(Value);
                floatProperty = Convert.ToSingle(property.GetValue());
            }

            switch (Operator)
            {
                case "==":
                    if (property.GetValue().Equals(floatValue == null ? Value : floatValue))
                        return true;
                    break;
                case "!=":
                    if (property.GetValue().Equals(floatValue == null ? Value : floatValue))
                        return true;
                    break;
                case ">":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty > floatValue)
                        return true;
                    break;
                case "<":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty < floatValue)
                        return true;
                    break;
                case ">=":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty >= floatValue)
                        return true;
                    break;
                case "<=":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty <= floatValue)
                        return true;
                    break;
            }
            return false;
        }
    }

}
