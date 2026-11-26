using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using RestSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Is the value of the property saved when saving (serializing) the entity? 
    /// Can be set to false if e.g. the value doesn't ever change from the prefab value, or if changes to it shouldn't persist between rounds.
    /// </summary>
    public enum IsPropertySaveable
    {
        Yes,
        No
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CustomDefaultValueAttribute(Type type) : Attribute
    {
        public object GetCustomDefaultValue(object entity)
        {
            MethodInfo defaultValueMethod = type.GetMethod(nameof(ICustomDefaultValueProvider.GetDefaultValue), BindingFlags.Public | BindingFlags.Static);
            if (defaultValueMethod is null)
            {
                throw new InvalidOperationException($"The type {type.FullName} does not implement the required static method GetDefaultValue.");
            }

            return defaultValueMethod.Invoke(null, [entity]);
        }
    }

    public interface ICustomDefaultValueProvider
    {
        static abstract object GetDefaultValue(object obj);
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class Serialize : Attribute
    {
        public readonly object DefaultValue;
        public readonly IsPropertySaveable IsSaveable;
        public readonly Identifier TranslationTextTag;

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
        /// <param name="translationTextTag">If set to anything else than null, SerializableEntityEditors will show what the text gets translated to or warn if the text is not found in the language files.</param>
        /// <param name="alwaysUseInstanceValues">If set to true, the instance values saved in a submarine file will always override the prefab values, even if using a mod that normally overrides instance values.
        /// Setting the value to a non-empty string will let the user select the text from one whose tag starts with the given string (e.g. RoomName. would show all texts with a RoomName.* tag)</param>
        public Serialize(object defaultValue, IsPropertySaveable isSaveable, string description = "", string translationTextTag = "", bool alwaysUseInstanceValues = false)
        {
            DefaultValue = defaultValue;
            IsSaveable = isSaveable;
            TranslationTextTag = translationTextTag.ToIdentifier();
            Description = description;
            AlwaysUseInstanceValues = alwaysUseInstanceValues;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class Header : Attribute
    {
        public readonly LocalizedString Text;

        public Header(string text = "", string localizedTextTag = null)
        {
            Text = localizedTextTag != null ? TextManager.Get(localizedTextTag) : text;
        }
    }

    public sealed class SerializableProperty
    {
        private static readonly ImmutableDictionary<Type, string> supportedTypes = new Dictionary<Type, string>
        {
            { typeof(bool), "bool" },
            { typeof(int), "int" },
            { typeof(ushort), "ushort" },
            { typeof(float), "float" },
            { typeof(string), "string" },
            { typeof(Identifier), "identifier" },
            { typeof(LanguageIdentifier), "languageidentifier" },
            { typeof(LocalizedString), "localizedstring" },
            { typeof(Point), "point" },
            { typeof(Vector2), "vector2" },
            { typeof(Vector3), "vector3" },
            { typeof(Vector4), "vector4" },
            { typeof(Rectangle), "rectangle" },
            { typeof(Color), "color" },
            { typeof(string[]), "stringarray" },
            { typeof(Identifier[]), "identifierarray" }
        }.ToImmutableDictionary();

        private static readonly Dictionary<Type, Dictionary<Identifier, SerializableProperty>> cachedProperties = 
            new Dictionary<Type, Dictionary<Identifier, SerializableProperty>>();
        public readonly string Name;
        public readonly Attribute[] Attributes;
        public readonly Type PropertyType;

        public readonly bool OverridePrefabValues;

        public readonly PropertyInfo PropertyInfo;

        public SerializableProperty(PropertyInfo property)
        {
            Name = property.Name;
            PropertyInfo = property;
            PropertyType = property.PropertyType;
            Attributes = property.GetCustomAttributes().ToArray();
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
                        DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value} (not a valid {PropertyInfo.PropertyType})", e);
                        return false;
                    }
                    try
                    {
                        PropertyInfo.SetValue(parentObject, enumVal);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value}", e);
                        return false;
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value} (Type \"{PropertyType.Name}\" not supported)");

                    return false;
                }
            }

            try
            {
                switch (typeName)
                {
                    case "bool":
                        bool boolValue = value.ToIdentifier() == "true";
                        if (TrySetBoolValueWithoutReflection(parentObject, boolValue)) { return true; }
                        PropertyInfo.SetValue(parentObject, boolValue, null);
                        break;
                    case "int":
                        if (int.TryParse(value, out int intVal))
                        {
                            if (TrySetFloatValueWithoutReflection(parentObject, intVal)) { return true; }
                            PropertyInfo.SetValue(parentObject, intVal, null);
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    case "ushort":
                        if (ushort.TryParse(value, out ushort ushortVal))
                        {
                            PropertyInfo.SetValue(parentObject, ushortVal, null);
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    case "float":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
                        {
                            if (TrySetFloatValueWithoutReflection(parentObject, floatVal)) { return true; }
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
                    case "identifier":
                        PropertyInfo.SetValue(parentObject, value.ToIdentifier());
                        break;
                    case "languageidentifier":
                        PropertyInfo.SetValue(parentObject, value.ToLanguageIdentifier());
                        break;
                    case "localizedstring":
                        PropertyInfo.SetValue(parentObject, new RawLString(value));
                        break;
                    case "stringarray":
                        PropertyInfo.SetValue(parentObject, ParseStringArray(value));
                        break;
                    case "identifierarray":
                        PropertyInfo.SetValue(parentObject, ParseIdentifierArray(value));
                        break;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value}", e);
                return false;
            }
            return true;
        }


        private static string[] ParseStringArray(string stringArrayValues)
        {
            return string.IsNullOrEmpty(stringArrayValues) ? Array.Empty<string>() : stringArrayValues.Split(';');
        }

        private static Identifier[] ParseIdentifierArray(string stringArrayValues)
        {
            return ParseStringArray(stringArrayValues).ToIdentifiers();
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
                            DebugConsole.ThrowError(
                                $"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value} (not a valid {PropertyInfo.PropertyType})", e);
                            return false;
                        }
                        PropertyInfo.SetValue(parentObject, enumVal);
                        return true;
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value} (Type \"{PropertyType.Name}\" not supported)");

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
                            case "identifier":
                                PropertyInfo.SetValue(parentObject, new Identifier((string)value));
                                return true;
                            case "languageidentifier":
                                PropertyInfo.SetValue(parentObject, ((string)value).ToLanguageIdentifier());
                                return true;
                            case "localizedstring":
                                PropertyInfo.SetValue(parentObject, new RawLString((string)value));
                                return true;
                            case "stringarray":
                                PropertyInfo.SetValue(parentObject, ParseStringArray((string)value));
                                return true;
                            case "identifierarray":
                                PropertyInfo.SetValue(parentObject, ParseIdentifierArray((string)value));
                                return true;
                            default:
                                DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value}");
                                DebugConsole.ThrowError($"(Cannot convert a string to a {PropertyType})");
                                return false;
                        }
                    }
                    else if (PropertyType != value.GetType())
                    {
                        DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value}");
                        DebugConsole.ThrowError("(Non-matching type, should be " + PropertyType + " instead of " + value.GetType() + ")");
                        return false;
                    }

                    PropertyInfo.SetValue(parentObject, value, null);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Failed to set the value of the property \"{Name}\" of \"{parentObject}\" to {value}", e);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error in SerializableProperty.TrySetValue (Property: {PropertyInfo.Name})", e);
                return false;
            }
        }

        public bool TrySetValue(object parentObject, float value)
        {
            try
            {
                if (TrySetFloatValueWithoutReflection(parentObject, value)) { return true; }
                PropertyInfo.SetValue(parentObject, value, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.TrySetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error in SerializableProperty.TrySetValue (Property: {PropertyInfo.Name})", e);
                return false;
            }

            return true;
        }

        public bool TrySetValue(object parentObject, bool value)
        {
            try
            {
                if (TrySetBoolValueWithoutReflection(parentObject, value)) { return true; }
                PropertyInfo.SetValue(parentObject, value, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.TrySetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error in SerializableProperty.TrySetValue (Property: {PropertyInfo.Name})", e);
                return false;
            }
            return true;
        }

        public bool TrySetValue(object parentObject, int value)
        {
            try
            {
                if (TrySetFloatValueWithoutReflection(parentObject, value)) { return true; }
                PropertyInfo.SetValue(parentObject, value, null);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.TrySetValue", e.InnerException);
                return false;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error in SerializableProperty.TrySetValue (Property: {PropertyInfo.Name})", e);
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

        public float GetFloatValue(object parentObject)
        {
            if (parentObject == null || PropertyInfo == null) { return 0.0f; }

            if (TryGetFloatValueWithoutReflection(parentObject, out float value))
            {
                return value;
            }

            try
            {
                if (PropertyType == typeof(int))
                {
                    return (int)PropertyInfo.GetValue(parentObject, null);
                }
                else
                {
                    return (float)PropertyInfo.GetValue(parentObject, null);
                }
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Exception thrown by the target of SerializableProperty.GetValue", e.InnerException);
                return 0.0f;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error in SerializableProperty.GetValue", e);
                return 0.0f;
            }
        }

        public bool GetBoolValue(object parentObject)
        {
            if (parentObject == null || PropertyInfo == null) { return false; }

            if (TryGetBoolValueWithoutReflection(parentObject, out bool value))
            {
                return value;
            }

            try
            {
                return (bool)PropertyInfo.GetValue(parentObject, null);
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
            if (type.IsEnum) { return "Enum"; }
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
            if (PropertyType == typeof(float))
            {
                if (TryGetFloatValueWithoutReflection(parentObject, out float value)) { return value; }
            }
            else if (PropertyType == typeof(bool))
            {
                if (TryGetBoolValueWithoutReflection(parentObject, out bool value)) { return value; }
            }
            else if (PropertyType == typeof(string))
            {
                if (TryGetStringValueWithoutReflection(parentObject, out string value)) { return value; }
            }
            return null;
        }

        /// <summary>
        /// Try getting the values of some commonly used properties directly without reflection
        /// </summary>
        private bool TryGetFloatValueWithoutReflection(object parentObject, out float value)
        {
            value = 0.0f;
            switch (Name)
            {
                case nameof(Powered.Voltage):
                    {
                        if (parentObject is Powered powered) { value = powered.Voltage; return true; }
                    }
                    break;
                case nameof(Powered.RelativeVoltage):
                    {
                        if (parentObject is Powered powered) { value = powered.RelativeVoltage; return true; }
                    }
                    break;
                case nameof(Powered.CurrPowerConsumption):
                    {
                        if (parentObject is Powered powered) { value = powered.CurrPowerConsumption; return true; }
                    }
                    break;
                case nameof(PowerContainer.Charge):
                    {
                        if (parentObject is PowerContainer powerContainer) { value = powerContainer.Charge; return true; }
                    }
                    break;
                case nameof(Repairable.StressDeteriorationMultiplier):
                    {
                        if (parentObject is Repairable repairable) { value = repairable.StressDeteriorationMultiplier; return true; }
                    }
                    break;
                case nameof(PowerContainer.ChargePercentage):
                    {
                        if (parentObject is PowerContainer powerContainer) { value = powerContainer.ChargePercentage; return true; }
                    }
                    break;
                case nameof(PowerContainer.RechargeRatio):
                    {
                        if (parentObject is PowerContainer powerContainer) { value = powerContainer.RechargeRatio; return true; }
                    }
                    break;
                case nameof(ItemContainer.ContainedNonBrokenItemCount):
                    {
                        if (parentObject is ItemContainer itemContainer) { value = itemContainer.ContainedNonBrokenItemCount; return true; }
                    }
                    break;
                case nameof(Reactor.AvailableFuel):
                    { if (parentObject is Reactor reactor) { value = reactor.AvailableFuel; return true; } }
                    break;
                case nameof(Reactor.FissionRate):
                    { if (parentObject is Reactor reactor) { value = reactor.FissionRate; return true; } }
                    break;
                case nameof(Reactor.Temperature):
                    { if (parentObject is Reactor reactor) { value = reactor.Temperature; return true; } }
                    break;
                case nameof(Vent.OxygenFlow):
                    { if (parentObject is Vent vent) { value = vent.OxygenFlow; return true; } }
                    break;
                case nameof(Vent.TotalOxygenFlow):
                    { if (parentObject is Vent vent) { value = vent.TotalOxygenFlow; return true; } }
                    break;
                case nameof(Pump.CurrFlow):
                    { if (parentObject is Pump pump) { value = pump.CurrFlow; return true; } }
                    if (parentObject is OxygenGenerator oxygenGenerator) { value = oxygenGenerator.CurrFlow; return true; }
                    break;
                case nameof(Engine.CurrentBrokenVolume):
                    { if (parentObject is Engine engine) { value = engine.CurrentBrokenVolume; return true; } }
                    { if (parentObject is Pump pump) { value = pump.CurrentBrokenVolume; return true; } }
                    break;
                case nameof(Engine.CurrentVolume):
                    { if (parentObject is Engine engine) { value = engine.CurrentVolume; return true; } }
                    break;
                case nameof(Character.Oxygen):
                    { if (parentObject is Character character) { value = character.Oxygen; return true; } }
                    { if (parentObject is Hull hull) { value = hull.Oxygen; return true; } }
                    break;
                case nameof(Character.Health):
                    { if (parentObject is Character character) { value = character.Health; return true; } }
                    break;
                case nameof(Character.OxygenAvailable):
                    { if (parentObject is Character character) { value = character.OxygenAvailable; return true; } }
                    break;
                case nameof(Character.PressureProtection):
                    { if (parentObject is Character character) { value = character.PressureProtection; return true; } }
                    break;
                case nameof(Item.Condition):
                    { if (parentObject is Item item) { value = item.Condition; return true; } }
                    break;
                case nameof(Item.ConditionPercentage):
                    { if (parentObject is Item item) { value = item.ConditionPercentage; return true; } }
                    break;
                case nameof(Item.SightRange):
                    { if (parentObject is Item item) { value = item.SightRange; return true; } }
                    break;
                case nameof(Item.SoundRange):
                    { if (parentObject is Item item) { value = item.SoundRange; return true; } }
                    break;
                case nameof(Character.SpeedMultiplier):
                    { if (parentObject is Character character) { value = character.SpeedMultiplier; return true; } }
                    break;
                case nameof(Character.PropulsionSpeedMultiplier):
                    { if (parentObject is Character character) { value = character.PropulsionSpeedMultiplier; return true; } }
                    break;
                case nameof(Character.LowPassMultiplier):
                    { if (parentObject is Character character) { value = character.LowPassMultiplier; return true; } }
                    break;
                case nameof(Character.ObstructVisionAmount):
                    { if (parentObject is Character character) { value = character.ObstructVisionAmount; return true; } }
                    break;
                case nameof(Character.HullOxygenPercentage):
                    {
                        if (parentObject is Character character) 
                        { 
                            value = character.HullOxygenPercentage;
                            return true;
                        }
                        else if (parentObject is Item item)
                        {
                            value = item.HullOxygenPercentage;
                            return true;
                        }
                    }
                    break;
                case nameof(Door.Stuck):
                    { if (parentObject is Door door) { value = door.Stuck; return true; } }
                    break;
                case nameof(Planter.PlantLightAmount):
                    { if (parentObject is Planter planter) { value = planter.PlantLightAmount; return true; } }
                    break;
            }
            return false;
        }
        
        /// <summary>
         /// Try getting the values of some commonly used properties directly without reflection
         /// </summary>
        private bool TryGetBoolValueWithoutReflection(object parentObject, out bool value)
        {
            value = false;
            switch (Name)
            {
                case nameof(ItemComponent.IsActive):
                    if (parentObject is ItemComponent ic) { value = ic.IsActive; return true; }
                    break;
                case nameof(PowerTransfer.Overload):
                    if (parentObject is PowerTransfer powerTransfer) { value = powerTransfer.Overload; return true; }
                    break;
                case nameof(PowerContainer.OutputDisabled):
                    if (parentObject is PowerContainer powerContainer) { value = powerContainer.OutputDisabled; return true; }
                    break;
                case nameof(MotionSensor.MotionDetected):
                    if (parentObject is MotionSensor motionSensor) { value = motionSensor.MotionDetected; return true; }
                    break;
                case nameof(Character.IsDead):
                    { if (parentObject is Character character) { value = character.IsDead; return true; } }
                    break;
                case nameof(Character.NeedsAir):
                    { if (parentObject is Character character) { value = character.NeedsAir; return true; } }
                    break;
                case nameof(Character.NeedsOxygen):
                    { if (parentObject is Character character) { value = character.NeedsOxygen; return true; } }
                    break;
                case nameof(Character.IsHuman):
                    { if (parentObject is Character character) { value = character.IsHuman; return true; } }
                    break;
                case nameof(LightComponent.IsOn):
                    { if (parentObject is LightComponent lightComponent) { value = lightComponent.IsOn; return true; } }
                    break;
                case nameof(Item.PhysicsBodyActive):
                    {
                        if (parentObject is Item item) { value = item.PhysicsBodyActive; return true; }
                    }
                    break;
                case nameof(DockingPort.Docked):
                    if (parentObject is DockingPort dockingPort) { value = dockingPort.Docked; return true; }
                    break;
                case nameof(Reactor.TemperatureCritical):
                    if (parentObject is Reactor reactor) { value = reactor.TemperatureCritical; return true; }
                    break;
                case nameof(TriggerComponent.TriggerActive):
                    if (parentObject is TriggerComponent trigger) { value = trigger.TriggerActive; return true; }
                    break;
                case nameof(Controller.State):
                    if (parentObject is Controller controller) { value = controller.State; return true; }
                    break;
                case nameof(Holdable.Attached):
                    if (parentObject is Holdable holdable) { value = holdable.Attached; return true; }
                    break;
                case nameof(Character.InWater):
                    {
                        if (parentObject is Character character)
                        {
                            value = character.InWater;
                            return true;
                        }
                        else if (parentObject is Item item)
                        {
                            value = item.InWater;
                            return true;
                        }
                    }
                    break;
                case nameof(Rope.Snapped):
                    if (parentObject is Rope rope) { value = rope.Snapped; return true; }
                    break;
            }
            return false;
        }

        /// <summary>
        /// Try getting the values of some commonly used properties directly without reflection
        /// </summary>
        private bool TryGetStringValueWithoutReflection(object parentObject, out string value)
        {
            value = null;
            switch (Name)
            {
                case nameof(Item.ContainerIdentifier):
                    {
                        if (parentObject is Item item) { value = item.ContainerIdentifier.Value; return true; }
                    }
                    break;
            }
            return false;
        }

        /// <summary>
        /// Try setting the values of some commonly used properties directly without reflection
        /// </summary>
        private bool TrySetFloatValueWithoutReflection(object parentObject, float value)
        {
            switch (Name)
            {
                case nameof(Item.Condition):
                    { if (parentObject is Item item) { item.Condition = value; return true; } }
                    break;
                case nameof(Powered.Voltage):
                    if (parentObject is Powered powered) { powered.Voltage = value; return true; }
                    break;
                case nameof(PowerContainer.Charge):
                    if (parentObject is PowerContainer powerContainer) { powerContainer.Charge = value; return true; }
                    break;
                case nameof(Reactor.AvailableFuel):
                    if (parentObject is Reactor reactor) { reactor.AvailableFuel = value; return true; }
                    break;
                case nameof(Character.Oxygen):
                    { if (parentObject is Character character) { character.Oxygen = value; return true; } }
                    break;
                case nameof(Character.OxygenAvailable):
                    { if (parentObject is Character character) { character.OxygenAvailable = value; return true; } }
                    break;
                case nameof(Character.PressureProtection):
                    { if (parentObject is Character character) { character.PressureProtection = value; return true; } }
                    break;
                case nameof(Character.LowPassMultiplier):
                    { if (parentObject is Character character) { character.LowPassMultiplier = value; return true; } }
                    break;
                case nameof(Character.SpeedMultiplier):
                    { if (parentObject is Character character) { character.StackSpeedMultiplier(value); return true; } }
                    break;
                case nameof(Character.HealthMultiplier):
                    { if (parentObject is Character character) { character.StackHealthMultiplier(value); return true; } }
                    break;
                case nameof(Character.PropulsionSpeedMultiplier):
                    { if (parentObject is Character character) { character.PropulsionSpeedMultiplier = value; return true; } }
                    break;
                case nameof(Character.ObstructVisionAmount):
                    { if (parentObject is Character character) { character.ObstructVisionAmount = value; return true; } }
                    break;
                case nameof(Item.Scale):
                    { if (parentObject is Item item) { item.Scale = value; return true; } }
                    break;
                case nameof(Item.SightRange):
                    { if (parentObject is Item item) { item.SightRange = value; return true; } }
                    break;
                case nameof(Item.SoundRange):
                    { if (parentObject is Item item) { item.SoundRange = value; return true; } }
                    break;
            }
            return false;
        }
        /// <summary>
        /// Try setting the values of some commonly used properties directly without reflection
        /// </summary>
        private bool TrySetBoolValueWithoutReflection(object parentObject, bool value)
        {
            switch (Name)
            {
                case nameof(Character.ObstructVision):
                    { if (parentObject is Character character) { character.ObstructVision = value; return true; } }
                    break;
                case nameof(Character.HideFace):
                    { if (parentObject is Character character) { character.HideFace = value; return true; } }
                    break;
                case nameof(Character.UseHullOxygen):
                    { if (parentObject is Character character) { character.UseHullOxygen = value; return true; } }
                    break;
                case nameof(LightComponent.IsOn):
                    { if (parentObject is LightComponent lightComponent) { lightComponent.IsOn = value; return true; } }
                    break;
                case nameof(ItemComponent.IsActive):
                    { if (parentObject is ItemComponent ic) { ic.IsActive = value; return true; } }
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

        public static Dictionary<Identifier, SerializableProperty> GetProperties(object obj)
        {
            Type objType = obj.GetType();
            if (cachedProperties.ContainsKey(objType))
            {
                return cachedProperties[objType];
            }

            var properties = obj.GetType().GetProperties();
            Dictionary<Identifier, SerializableProperty> dictionary = new Dictionary<Identifier, SerializableProperty>();
            foreach (var property in properties)
            {
                SerializableProperty serializableProperty = null;
                try
                {
                    //if the getter is private, we must get it from the declaring type to access it and check if it exists
                    var propertyFromDeclaringType = property.DeclaringType.GetProperty(property.Name);
                    if (propertyFromDeclaringType.GetMethod is null) { continue; }
                    serializableProperty = new SerializableProperty(propertyFromDeclaringType);
                }
                catch (AmbiguousMatchException)
                {
                    //can happen e.g. with AnimController.CurrentGroundedParams, which is of an abstract type -
                    //let's just ignore these types of properties (you can't really do anything with SerializableProperties that are reference types anyway)
                    continue;
                }
                dictionary.Add(serializableProperty.Name.ToIdentifier(), serializableProperty);
            }

            cachedProperties[objType] = dictionary;

            return dictionary;
        }
        
        public static Dictionary<Identifier, SerializableProperty> DeserializeProperties(object obj, XElement element = null)
        {
            Dictionary<Identifier, SerializableProperty> dictionary = GetProperties(obj);
#if DEBUG
            var nonPublicProperties = obj.GetType().GetProperties(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var property in nonPublicProperties)
            {
                if (property.GetAttribute<Serialize>() != null)
                {
                    DebugConsole.ThrowError($"The property {property.Name} in class {obj.GetType()} is set as serializable, but isn't public. Serializable properties must have at least a public getter.");
                }
            }
#endif
            foreach (var property in dictionary.Values)
            {
                if (property.GetAttribute<CustomDefaultValueAttribute>() is { } customDefaultValueProvider)
                {
                    property.TrySetValue(obj, customDefaultValueProvider.GetCustomDefaultValue(obj));
                    continue;
                }

                //set the value of the property to the default value if there is one
                foreach (var ini in property.Attributes.OfType<Serialize>())
                {
                    property.TrySetValue(obj, ini.DefaultValue);
                    break;
                }
            }

            if (element != null)
            {
                //go through all the attributes in the xml element 
                //and set the value of the matching property if it is initializable
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (!dictionary.TryGetValue(attribute.NameAsIdentifier(), out SerializableProperty property)) { continue; }
                    if (!property.Attributes.OfType<Serialize>().Any()) { continue; }
                    property.TrySetValue(obj, attribute.Value);
                }
            }

            return dictionary;
        }

        /// <summary>
        /// Deserializes a single named property on the given object from the XML element if a matching attribute exists,
        /// otherwise resets it to the default.
        /// Basically a singular version of <see cref="DeserializeProperties"/>.
        /// </summary>
        public static void DeserializeProperty(object obj, string propertyName, XElement element = null)
        {
            if (!GetProperties(obj).TryGetValue(propertyName.ToIdentifier(), out SerializableProperty property))
            {
#if DEBUG
                // Developer QoL
                var nonPublicProperty = obj.GetType().GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (nonPublicProperty?.GetAttribute<Serialize>() != null)
                {
                    DebugConsole.ThrowError($"The property {property.Name} in class {obj.GetType()} is set as serializable, but isn't public. Serializable properties must have at least a public getter.");
                }
#endif
                throw new KeyNotFoundException($"No serializable property named \"{propertyName}\" on {obj.GetType()}.");
            }

            // Find the matching attribute in the XML element, and reset the value of the property to that (if it is initializable)
            if (element?.GetAttribute(propertyName) is XAttribute attribute)
            {
                property.TrySetValue(obj, attribute.Value);
                return;
            }

            // Otherwise, set the value of the property to the default value if there is one
            if (property.GetAttribute<CustomDefaultValueAttribute>() is { } customDefaultValueProvider)
            {
                property.TrySetValue(obj, customDefaultValueProvider.GetCustomDefaultValue(obj));
            }
            else
            {
                if (property.Attributes.OfType<Serialize>().FirstOrDefault() is { } serialize)
                {
                    property.TrySetValue(obj, serialize.DefaultValue);
                }
            }

        }

        public static void AdditionalDeserializeProperties(object obj, XElement element = null)
        {
            Dictionary<Identifier, SerializableProperty> dictionary = GetProperties(obj);

            if (element != null)
            {
                //go through all the attributes in the xml element 
                //and set the value of the matching property if it is initializable
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (!dictionary.TryGetValue(attribute.NameAsIdentifier(), out SerializableProperty property)) { continue; }
                    if (property.Attributes.OfType<Serialize>().FirstOrDefault() is not { } serialize) { continue; }
                    string defaultValue = serialize.DefaultValue.ToString() ?? string.Empty;
                    if (string.Equals(defaultValue, attribute.Value, StringComparison.OrdinalIgnoreCase)) { continue; }
                    property.TrySetValue(obj, attribute.Value);
                }
            }
        }

        public static void SerializeProperties(ISerializableEntity obj, XElement element, bool saveIfDefault = false, bool ignoreEditable = false)
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
                        if ((attribute.IsSaveable == IsPropertySaveable.Yes && !XMLExtensions.DefaultValueEquals(attribute.DefaultValue, value)) ||
                            (!ignoreEditable && property.Attributes.OfType<Editable>().Any()))
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
                        case "stringarray":
                            string[] stringArray = (string[])value;
                            stringValue =  stringArray != null ? string.Join(';', stringArray) : "";
                            break;
                        case "identifierarray":
                            Identifier[] identifierArray = (Identifier[])value;
                            stringValue =  identifierArray != null ? string.Join(';', identifierArray) : "";
                            break;
                        default:
                            stringValue = value.ToString();
                            break;
                    }
                }
                element.GetAttribute(property.Name)?.Remove();
                element.SetAttributeValue(property.Name, stringValue);
            }
        }

        /// <summary>
        /// Upgrade the properties of an entity saved with an older version of the game. Properties that should be upgraded are defined using "Upgrade" elements in the config file.
        /// for example, <Upgrade gameversion="0.9.2.0" scale="0.5"/> would force the scale of the entity to 0.5 if it was saved with a version prior to 0.9.2.0.
        /// An upgrade can also target a specific component of an item, either by defining it inside the component:
        /// eg. <Repairable><Upgrade gameversion="0.9.2.0" RepairThreshold="80"/></Repairable>
        /// or by naming the component inside the Upgrade element:
        /// eg. <Upgrade gameversion="0.9.2.0"><Repairable RepairThreshold="80"/></Upgrade>
        /// NOTE (especially for Holdable): Upgrades inside a component are applied while the component is loading, item-level
        /// ones only after all the components have loaded. Holdable caches the message and the required items while loading
        /// and then restores them when attached, so it has to be upgraded from inside the component.
        /// </summary>
        /// <param name="entity">The entity to upgrade</param>
        /// <param name="configElement">The XML element to get the upgrade instructions from (e.g. the config of an item prefab)</param>
        /// <param name="savedVersion">The game version the entity was saved with</param>
        public static void UpgradeGameVersion(ISerializableEntity entity, ContentXElement configElement, Version savedVersion)
        {
            foreach (var subElement in configElement.Elements())
            {
                if (!subElement.Name.ToString().Equals("upgrade", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (subElement.GetAttribute("gameversion") == null)
                {
                    // Note that empty Upgrade elements seem to be also used to get rid of previous Upgrades they'd otherwise
                    // inherit from the base prefab. And though normally both the empty element and the one it removes don't
                    // make it to the actual merged XML, if the base prefab has no matching element to remove, then the
                    // empty element is copied as-is and ends up here.
                    // But if it is incomplete rather than fully empty, let's make noise about that.
                    if (subElement.Attributes().Any() || subElement.Elements().Any())
                    {
                        DebugConsole.ThrowError($"Error in the config of '{entity.Name}': an upgrade element with no 'gameversion' attribute. This upgrade will never get applied.",
                            contentPackage: subElement.ContentPackage);
                    }
                    continue;
                }
                string upgradeVersionStr = subElement.GetAttributeString("gameversion", "0.0.0.0");
                if (!Version.TryParse(upgradeVersionStr, out Version upgradeVersion))
                {
                    DebugConsole.ThrowError($"Error in the config of '{entity.Name}': '{upgradeVersionStr}' is not a valid 'gameversion' value. This upgrade will never get applied.",
                        contentPackage: subElement.ContentPackage);
                    continue;
                }
                if (subElement.GetAttributeBool("campaignsaveonly", false))
                {
                    if ((GameMain.GameSession?.LastSaveVersion ?? GameMain.Version) >= upgradeVersion) { continue; }
                }
                else
                {
                    if (savedVersion >= upgradeVersion) { continue; }
                }
                foreach (XAttribute attribute in subElement.Attributes())
                {
                    var attributeName = attribute.NameAsIdentifier();
                    if (attributeName == "gameversion" || attributeName == "campaignsaveonly") { continue; }

                    bool applied = false;
                    if (attributeName == "refreshrect")
                    {
                        applied = true;
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
                        else if (entity is Item item)
                        {
                            if (!item.ResizeHorizontal)
                            {
                                item.Rect = item.DefaultRect = new Rectangle(item.Rect.X, item.Rect.Y,
                                    (int)(item.Prefab.Size.X * item.Prefab.Scale),
                                    item.Rect.Height);
                            }
                            if (!item.ResizeVertical)
                            {
                                item.Rect = item.DefaultRect = new Rectangle(item.Rect.X, item.Rect.Y,
                                    item.Rect.Width,
                                    (int)(item.Prefab.Size.Y * item.Prefab.Scale));
                            }
                        }
                    }
                    else if (attributeName == "unlockrecipe" || attributeName == "unlockrecipes")
                    {
                        applied = true;
                        var recipes = subElement.GetAttributeIdentifierImmutableHashSet("unlockrecipes",
                            def: subElement.GetAttributeIdentifierImmutableHashSet("unlockrecipe", ImmutableHashSet<Identifier>.Empty));
                        foreach (var recipe in recipes)
                        {
                            GameMain.GameSession?.UnlockRecipe(CharacterTeamType.Team1, recipe, showNotifications: false);
                        }
                    }

                    if (entity.SerializableProperties.TryGetValue(attributeName, out SerializableProperty property))
                    {
                        // The entity here can also be an ItemComponent (instead of Item) when the Upgrade is defined inside
                        // the component, so we apply the upgrade attributes here.
                        ApplyUpgradeValue(property, entity, attribute);
                        applied = true;
                    }
                    else if (entity is Item item1)
                    {
                        foreach (ISerializableEntity component in item1.AllPropertyObjects)
                        {
                            if (component.SerializableProperties.TryGetValue(attributeName, out SerializableProperty componentProperty))
                            {
                                ApplyUpgradeValue(componentProperty, component, attribute);
                                applied = true;
                            }
                        }
                    }
                    if (!applied)
                    {
                        WarnAboutUnknownProperty(entity, attributeName, subElement);
                    }
                }

                static void WarnAboutUnknownProperty(ISerializableEntity target, Identifier attributeName, ContentXElement upgradeElement)
                {
                    // Only warn on the client, some properties don't exist on the server at all
#if CLIENT
                    string targetName = target is ItemComponent component ?
                        $"{component.Item.Name} ({component.Name})" :
                        target.Name;
                    DebugConsole.AddWarning($"Potential error in the config of '{targetName}': an Upgrade sets '{attributeName}', but there's no property with that name.",
                        contentPackage: upgradeElement.ContentPackage);
#endif
                }

                static void ApplyUpgradeValue(SerializableProperty property, object parentObject, XAttribute attribute)
                {
                    FixValue(property, parentObject, attribute);
                    if (property.Name == nameof(ItemComponent.Msg) && parentObject is ItemComponent component)
                    {
                        component.ParseMsg();
                    }
                }

                static void ApplyUpgradeToComponent(ItemComponent itemComponent, ContentXElement upgradeElement, bool applyAttributes)
                {
                    if (applyAttributes)
                    {
                        foreach (XAttribute attribute in upgradeElement.Attributes())
                        {
                            var attributeName = attribute.NameAsIdentifier();
                            if (itemComponent.SerializableProperties.TryGetValue(attributeName, out SerializableProperty property))
                            {
                                ApplyUpgradeValue(property, itemComponent, attribute);
                            }
                            else
                            {
                                WarnAboutUnknownProperty(itemComponent, attributeName, upgradeElement);
                            }
                        }
                    }

                    bool requiredItemsCleared = false;
                    foreach (var element in upgradeElement.Elements())
                    {
                        switch (element.Name.ToString().ToLowerInvariant())
                        {
                            case "requireditem":
                            case "requireditems":
                                if (!requiredItemsCleared)
                                {
                                    // The required items in the upgrade replace the existing ones, but only clear them
                                    // once so several required items in the same upgrade don't overwrite each other
                                    itemComponent.RequiredItems.Clear();
                                    itemComponent.DisabledRequiredItems.Clear();
                                    requiredItemsCleared = true;
                                }
                                itemComponent.SetRequiredItems(element, allowEmpty: true);
                                break;
                        }
                    }
                    if (itemComponent is ItemContainer itemContainer &&
                        (upgradeElement.GetChildElement("containable") != null || upgradeElement.GetChildElement("subcontainer") != null))
                    {
                        itemContainer.ReloadContainableRestrictions(upgradeElement);
                    }
                }

                static void FixValue(SerializableProperty property, object parentObject, XAttribute attribute)
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
                    else if (attribute.Value.Length > 0 && attribute.Value[0] == '+')
                    {
                        if (property.PropertyType == typeof(int))
                        {
                            float.TryParse(attribute.Value.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float addition);
                            property.TrySetValue(parentObject, (int)(((int)property.GetValue(parentObject)) + addition));
                        }
                        else if (property.PropertyType == typeof(float))
                        {
                            float.TryParse(attribute.Value.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float addition);
                            property.TrySetValue(parentObject, (float)property.GetValue(parentObject) + addition);
                        }
                        else if (property.PropertyType == typeof(Vector2))
                        {
                            var addition = XMLExtensions.ParseVector2(attribute.Value.Substring(1));
                            property.TrySetValue(parentObject, (Vector2)property.GetValue(parentObject) + addition);
                        }
                        else if (property.PropertyType == typeof(Point))
                        {
                            var addition = XMLExtensions.ParsePoint(attribute.Value.Substring(1));
                            property.TrySetValue(parentObject, ((Point)property.GetValue(parentObject)) + addition);
                        }
                    }
                    else
                    {
                        property.TrySetValue(parentObject, attribute.Value);
                    }
                }

                if (entity is Item item2)
                {
                    // The child elements of an item's upgrade are the components the upgrade applies to
                    foreach (var componentElement in subElement.Elements())
                    {
                        ItemComponent itemComponent = item2.Components.FirstOrDefault(c => c.Name.Equals(componentElement.Name.ToString(), StringComparison.OrdinalIgnoreCase));
                        if (itemComponent == null)
                        {
                            DebugConsole.ThrowError($"Error in the config of '{item2.Name}': an upgrade tries to apply to the component '{componentElement.Name}', but the item has no such component.",
                                contentPackage: subElement.ContentPackage);
                            continue;
                        }
                        ApplyUpgradeToComponent(itemComponent, componentElement, applyAttributes: true);
                    }
                }
                else if (entity is ItemComponent upgradedComponent)
                {
                    // The attributes of an upgrade defined inside a component have already been applied above in the
                    // attribute loop. Only the upgrade's child elements are left here.
                    ApplyUpgradeToComponent(upgradedComponent, subElement, applyAttributes: false);
                }
            }
        }

        public static void PurgeCache()
            => cachedProperties.Clear();
    }
}
