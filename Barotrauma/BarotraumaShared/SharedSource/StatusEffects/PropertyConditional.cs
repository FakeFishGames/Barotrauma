using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    // TODO: This class should be refactored: 
    // - Use XElement instead of XAttribute in the constructor
    // - Simplify, remove unnecessary conversions
    // - Improve the flow so that the logic is undestandable.
    // - Maybe add some test cases for the operators?
    class PropertyConditional
    {
        public enum ConditionType
        {
            Uncertain,
            PropertyValue,
            Name,
            SpeciesName,
            SpeciesGroup,
            HasTag,
            HasStatusTag,
            HasSpecifierTag,
            Affliction,
            EntityType,
            LimbType,
            SkillRequirement
        }

        public enum Comparison
        {
            And,
            Or
        }

        public enum OperatorType
        {
            None,
            Equals,
            NotEquals,
            LessThan,
            LessThanEquals,
            GreaterThan,
            GreaterThanEquals
        }

        public readonly ConditionType Type;
        public readonly OperatorType Operator;
        public readonly Identifier AttributeName;
        public readonly string AttributeValue;
        public readonly string[] SplitAttributeValue;
        public readonly float? FloatValue;

        public readonly string TargetItemComponentName;

        // Only used by attacks
        public readonly bool TargetSelf;

        // Only used by conditionals targeting an item (makes the conditional check the item/character whose inventory this item is inside)
        public readonly bool TargetContainer;
        // Only used by conditionals targeting an item. By default, containers check the parent item. This allows you to check the grandparent instead.
        public readonly bool TargetGrandParent;

        public readonly bool TargetContainedItem;

        // Remove this after refactoring
        public static bool IsValid(XAttribute attribute)
        {
            switch (attribute.Name.ToString().ToLowerInvariant())
            {
                case "targetitemcomponent":
                case "targetself":
                case "targetcontainer":
                case "targetgrandparent":
                case "targetcontaineditem":
                case "skillrequirement":
                    return false;
                default:
                    return true;
            }
        }

        // TODO: use XElement instead of XAttribute (how to do without breaking the existing content?)
        public PropertyConditional(XAttribute attribute)
        {
            AttributeName = attribute.NameAsIdentifier();
            string attributeValueString = attribute.Value;
            if (string.IsNullOrWhiteSpace(attributeValueString))
            {
                DebugConsole.ThrowError($"Conditional attribute value is empty: {attribute.Parent}");
                return;
            }
            string valueString = attributeValueString;
            string[] splitString = valueString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (splitString.Length > 1) { valueString = string.Join(' ', splitString.Skip(1)); }
            Operator = GetOperatorType(splitString[0]);

            if (Operator == OperatorType.None)
            {
                Operator = OperatorType.Equals;
                valueString = attributeValueString;
            }

            TargetItemComponentName = attribute.Parent.GetAttributeString("targetitemcomponent", "");
            TargetContainer = attribute.Parent.GetAttributeBool("targetcontainer", false);
            TargetSelf = attribute.Parent.GetAttributeBool("targetself", false);
            TargetGrandParent = attribute.Parent.GetAttributeBool("targetgrandparent", false);
            TargetContainedItem = attribute.Parent.GetAttributeBool("targetcontaineditem", false);

            if (!Enum.TryParse(AttributeName.Value, true, out Type))
            {
                Type = ConditionType.Uncertain;
            }

            if (attribute.Parent.GetAttributeBool("skillrequirement", false))
            {
                Type = ConditionType.SkillRequirement;
            }
            
            AttributeValue = valueString;
            SplitAttributeValue = valueString.Split(',');
            if (float.TryParse(AttributeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                FloatValue = value;
            }
        }

        public static OperatorType GetOperatorType(string op)
        {
            //thanks xml for not letting me use < or > in attributes :(
            switch (op)
            {
                case "e":
                case "eq":
                case "equals":
                    return OperatorType.Equals;
                case "ne":
                case "neq":
                case "notequals":
                case "!":
                case "!e":
                case "!eq":
                case "!equals":
                    return OperatorType.NotEquals;
                case "gt":
                case "greaterthan":
                    return OperatorType.GreaterThan;
                case "lt":
                case "lessthan":
                    return OperatorType.LessThan;
                case "gte":
                case "gteq":
                case "greaterthanequals":
                    return OperatorType.GreaterThanEquals;
                case "lte":
                case "lteq":
                case "lessthanequals":
                    return OperatorType.LessThanEquals;
                default:
                    return OperatorType.None;
            }
        }


        public bool Matches(ISerializableEntity target)
        {
            return Matches(target, TargetContainedItem);
        }

        public bool Matches(ISerializableEntity target, bool checkContained)
        {
            var type = Type;
            if (type == ConditionType.Uncertain)
            {
                type = AfflictionPrefab.Prefabs.ContainsKey(AttributeName)
                    ? ConditionType.Affliction
                    : ConditionType.PropertyValue;
            }
            
            if (checkContained)
            {
                if (target is Item item)
                {
                    foreach (var containedItem in item.ContainedItems)
                    {
                        if (Matches(containedItem, checkContained: false)) { return true; }
                    }
                    return false;
                }
                else if (target is Items.Components.ItemComponent ic)
                {
                    foreach (var containedItem in ic.Item.ContainedItems)
                    {
                        if (Matches(containedItem, checkContained: false)) { return true; }
                    }
                    return false;
                }
                else if (target is Character character)
                {
                    if (character.Inventory == null) { return false; }
                    foreach (var containedItem in character.Inventory.AllItems)
                    {
                        if (Matches(containedItem, checkContained: false)) { return true; }
                    }
                    return false;
                }
            }

            switch (type)
            {
                case ConditionType.PropertyValue:
                    SerializableProperty property;
                    if (target?.SerializableProperties == null) { return Operator == OperatorType.NotEquals; }
                    if (target.SerializableProperties.TryGetValue(AttributeName, out property))
                    {
                        return Matches(target, property);
                    }
                    return false;
                case ConditionType.Name:
                    if (target == null) { return Operator == OperatorType.NotEquals; }
                    return (Operator == OperatorType.Equals) == (target.Name == AttributeValue);
                case ConditionType.HasTag:
                    if (target == null) { return Operator == OperatorType.NotEquals; }
                    return MatchesTagCondition(target);
                case ConditionType.HasStatusTag:
                    if (target == null) { return Operator == OperatorType.NotEquals; }
                    int matches = 0;
                    foreach (DurationListElement durationEffect in StatusEffect.DurationList)
                    {
                        if (!durationEffect.Targets.Contains(target)) { continue; }
                        foreach (string tag in SplitAttributeValue)
                        {
                            if (durationEffect.Parent.HasTag(tag))
                            {
                                matches++;
                            }
                        }
                    }
                    foreach (DelayedListElement delayedEffect in DelayedEffect.DelayList)
                    {
                        if (!delayedEffect.Targets.Contains(target)) { continue; }
                        foreach (string tag in SplitAttributeValue)
                        {
                            if (delayedEffect.Parent.HasTag(tag))
                            {
                                matches++;
                            }
                        }
                    }
                    return Operator == OperatorType.Equals ? matches >= SplitAttributeValue.Length : matches <= 0;
                case ConditionType.HasSpecifierTag:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }
                        if (!(target is Character { Info: { } characterInfo })) { return false; }

                        return (Operator == OperatorType.Equals) ==
                               SplitAttributeValue.All(v => characterInfo.Head.Preset.TagSet.Contains(v));
                    }
                case ConditionType.SpeciesName:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }
                        if (!(target is Character targetCharacter)) { return false; }
                        return (Operator == OperatorType.Equals) == (targetCharacter.SpeciesName == AttributeValue);
                    }
                case ConditionType.SpeciesGroup:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }
                        if (!(target is Character targetCharacter)) { return false; }
                        return (Operator == OperatorType.Equals) == targetCharacter.Params.CompareGroup(AttributeValue.ToIdentifier());
                    }
                case ConditionType.EntityType:
                    switch (AttributeValue)
                    {
                        case "character":
                        case "Character":
                            return (Operator == OperatorType.Equals) == target is Character;
                        case "limb":
                        case "Limb":
                            return (Operator == OperatorType.Equals) == target is Limb;
                        case "item":
                        case "Item":
                            return (Operator == OperatorType.Equals) == target is Item;
                        case "structure":
                        case "Structure":
                            return (Operator == OperatorType.Equals) == target is Structure;
                        case "null":
                            return (Operator == OperatorType.Equals) == (target == null);
                        default:
                            return false;
                    }
                case ConditionType.LimbType:
                    {
                        if (!(target is Limb limb))
                        {
                            return false;
                        }
                        else
                        {
                            return limb.type.ToString().Equals(AttributeValue, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                case ConditionType.Affliction:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }

                        Character targetChar = target as Character;
                        if (target is Limb limb) { targetChar = limb.character; }
                        if (targetChar != null)
                        {
                            var health = targetChar.CharacterHealth;
                            if (health == null) { return false; }
                            var affliction = health.GetAffliction(AttributeName.ToIdentifier());
                            float afflictionStrength = affliction == null ? 0.0f : affliction.Strength;

                            return ValueMatchesRequirement(afflictionStrength);
                        }
                    }
                    return false;
                case ConditionType.SkillRequirement:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }

                        if (target is Character targetChar)
                        {
                            float skillLevel = targetChar.GetSkillLevel(AttributeName.ToIdentifier());

                            return ValueMatchesRequirement(skillLevel);
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }

        private bool ValueMatchesRequirement(float testedValue)
        {
            if (FloatValue.HasValue)
            {
                float value = FloatValue.Value;
                switch (Operator)
                {
                    case OperatorType.Equals:
                        return testedValue == value;
                    case OperatorType.GreaterThan:
                        return testedValue > value;
                    case OperatorType.GreaterThanEquals:
                        return testedValue >= value;
                    case OperatorType.LessThan:
                        return testedValue < value;
                    case OperatorType.LessThanEquals:
                        return testedValue <= value;
                    case OperatorType.NotEquals:
                        return testedValue != value;
                }
            }
            return false;
        }

        private bool MatchesTagCondition(ISerializableEntity target)
        {
            if (!(target is Item item)) { return Operator == OperatorType.NotEquals; }

            int matches = 0;
            foreach (string tag in SplitAttributeValue)
            {
                if (item.HasTag(tag))
                {
                    matches++;
                }
            }
            //If operator is == then it needs to match everything, otherwise if its != there must be zero matches.
            return Operator == OperatorType.Equals ? matches >= SplitAttributeValue.Length : matches <= 0;
        }

        public bool MatchesTagCondition(Identifier targetTag)
        {
            if (targetTag.IsEmpty || Type != ConditionType.HasTag) { return false; }

            int matches = 0;
            foreach (string tag in SplitAttributeValue)
            {
                if (targetTag == tag)
                {
                    matches++;
                }
            }
            //If operator is == then it needs to match everything, otherwise if its != there must be zero matches.
            return Operator == OperatorType.Equals ? matches >= SplitAttributeValue.Length : matches <= 0;
        }

        // TODO: refactor and add tests
        private bool Matches(ISerializableEntity target, SerializableProperty property)
        {
            Type type = property.PropertyType;

            if (type == typeof(float) || type == typeof(int))
            {
                float floatValue = property.GetFloatValue(target);
                switch (Operator)
                {
                    case OperatorType.Equals:
                        return MathUtils.NearlyEqual(floatValue, FloatValue.Value);
                    case OperatorType.NotEquals:
                        return !MathUtils.NearlyEqual(floatValue, FloatValue.Value);
                    case OperatorType.GreaterThan:
                        return floatValue > FloatValue.Value;
                    case OperatorType.LessThan:
                        return floatValue < FloatValue.Value;
                    case OperatorType.GreaterThanEquals:
                        return floatValue >= FloatValue.Value;
                    case OperatorType.LessThanEquals:
                        return floatValue <= FloatValue.Value;
                }
                return false;
            }

            switch (Operator)
            {
                case OperatorType.Equals:
                    {
                        if (type == typeof(bool))
                        {
                            return property.GetBoolValue(target) == (AttributeValue == "true" || AttributeValue == "True");
                        }
                        var value = property.GetValue(target);
                        return Equals(value, AttributeValue);
                    }
                case OperatorType.NotEquals:
                    {
                        if (type == typeof(bool))
                        {
                            return property.GetBoolValue(target) != (AttributeValue == "true" || AttributeValue == "True");
                        }
                        var value = property.GetValue(target);
                        return !Equals(value, AttributeValue);
                    }
                case OperatorType.GreaterThan:
                case OperatorType.LessThanEquals:
                case OperatorType.LessThan:
                case OperatorType.GreaterThanEquals:
                    DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                        + "Make sure the type of the value set in the config files matches the type of the property.");
                    break;
            }
            return false;

            static bool Equals(object value, string desiredValue)
            {
                if (value == null)
                {
                    return desiredValue.Equals("null", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    return value.ToString().Equals(desiredValue);
                }
            }
        }
    }

}
