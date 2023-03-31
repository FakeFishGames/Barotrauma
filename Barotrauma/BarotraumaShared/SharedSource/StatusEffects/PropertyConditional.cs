#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    /// <summary>
    /// Conditionals are used by some in-game mechanics to require one
    /// or more conditions to be met for those mechanics to be active.
    /// For example, some StatusEffects use Conditionals to only trigger
    /// if the affected character is alive.
    /// </summary>
    sealed class PropertyConditional
    {
        // TODO: Make this testable and add tests

        /// <summary>
        /// Category of properties to check against
        /// </summary>
        public enum ConditionType
        {
            /// <summary>
            /// Depending on what's available, check against either one
            /// of the target object's properties or the strength of an
            /// affliction.
            ///
            /// The target object's available properties depend on how that
            /// object is defined in the [source code](https://github.com/Regalis11/Barotrauma).
            ///
            /// This is not applicable if the element contains the attribute
            /// `SkillRequirement="true"`.
            /// </summary>
            /// <AutoDocEntryName value="Property or affliction identifier" />
            /// <example>
            /// <Conditional IsDead="true" />
            /// </example>
            PropertyValueOrAffliction,

            /// <summary>
            /// Check against the target character's skill with the same name as the attribute.
            ///
            /// This is only applicable if the element contains the attribute
            /// `SkillRequirement="true"`.
            /// </summary>
            /// <AutoDocEntryName value="Skill identifier" />
            /// <example>
            /// <Conditional SkillRequirement="true" weapons="lt 50" />
            /// </example>
            SkillRequirement,

            /// <summary>
            /// Check against the name of the target.
            /// </summary>
            Name,

            /// <summary>
            /// Check against the species identifier of the target. Only works on characters.
            /// </summary>
            SpeciesName,

            /// <summary>
            /// Check against the species group of the target. Only works on characters.
            /// </summary>
            SpeciesGroup,

            /// <summary>
            /// Check against the target's tags. Only works on items.
            ///
            /// Several tags can be checked against by using a comma-separated list.
            /// </summary>
            HasTag,

            /// <summary>
            /// Check against the tags of the target's active status effects.
            ///
            /// Several tags can be checked against by using a comma-separated list.
            /// </summary>
            HasStatusTag,

            /// <summary>
            /// Check against the target's specifier tags. In the vanilla game, these are the head index
            /// and gender. See human.xml for more details.
            ///
            /// Several tags can be checked against by using a comma-separated list.
            /// </summary>
            HasSpecifierTag,

            /// <summary>
            /// Check against the target's entity type.
            ///
            /// The currently supported values are "character", "limb", "item", "structure" and "null".
            /// </summary>
            EntityType,

            /// <summary>
            /// Check against the target's limb type. See <see cref="Barotrauma.LimbType"/>.
            /// </summary>
            LimbType
        }

        public enum LogicalOperatorType
        {
            And,
            Or
        }

        /// <summary>
        /// There are several ways to compare properties to values. The comparison operator
        /// to use can be specified by placing one of the following before the value to compare
        /// against.
        /// </summary>
        public enum ComparisonOperatorType
        {
            None,

            /// <summary>
            /// Require that the property being checked equals the given value.
            ///
            /// This is the default operator used if none is specified.
            /// </summary>
            Equals,

            /// <summary>
            /// Require that the property being checked doesn't equal the given value.
            /// </summary>
            NotEquals,

            /// <summary>
            /// Require that the property being checked is less than the given value.
            /// 
            /// This can only be used to compare with numeric object properties,
            /// affliction strengths and skill levels.
            /// </summary>
            LessThan,

            /// <summary>
            /// Require that the property being checked is less than or equal to the given value.
            /// 
            /// This can only be used to compare with numeric object properties,
            /// affliction strengths and skill levels.
            /// </summary>
            LessThanEquals,

            /// <summary>
            /// Require that the property being checked is greater than the given value.
            /// 
            /// This can only be used to compare with numeric object properties,
            /// affliction strengths and skill levels.
            /// </summary>
            GreaterThan,

            /// <summary>
            /// Require that the property being checked is greater than or equal to the given value.
            /// 
            /// This can only be used to compare with numeric object properties,
            /// affliction strengths and skill levels.
            /// </summary>
            GreaterThanEquals
        }

        public readonly ConditionType Type;
        public readonly ComparisonOperatorType ComparisonOperator;
        public readonly Identifier AttributeName;
        public readonly string AttributeValue;
        public readonly ImmutableArray<Identifier> AttributeValueAsTags;
        public readonly float? FloatValue;

        /// <summary>
        /// If set to the name of one of the target's ItemComponents, the conditionals defined by this element check against the properties of that component.
        /// Only works on items.
        /// </summary>
        public readonly string TargetItemComponent;

        /// <summary>
        /// If set to true, the conditionals defined by this element check against the attacking character instead of the attacked character
        /// </summary>
        public readonly bool TargetSelf;

        /// <summary>
        /// If set to true, the conditionals defined by this element check against the entity containing the target.
        /// </summary>
        public readonly bool TargetContainer;

        /// <summary>
        /// If this and TargetContainer are set to true, the conditionals defined by this element check against the entity containing the target's container.
        /// </summary>
        public readonly bool TargetGrandParent;

        /// <summary>
        /// If set to true, the conditionals defined by this element check against the items contained by the target. Only works with items.
        /// </summary>
        public readonly bool TargetContainedItem;

        public static IEnumerable<PropertyConditional> FromXElement(XElement element, Predicate<XAttribute>? predicate = null)
        {
            var targetItemComponent = element.GetAttributeString(nameof(TargetItemComponent), "");
            var targetContainer = element.GetAttributeBool(nameof(TargetContainer), false);
            var targetSelf = element.GetAttributeBool(nameof(TargetSelf), false);
            var targetGrandParent = element.GetAttributeBool(nameof(TargetGrandParent), false);
            var targetContainedItem = element.GetAttributeBool(nameof(TargetContainedItem), false);

            ConditionType? overrideConditionType = null;
            if (element.GetAttributeBool(nameof(ConditionType.SkillRequirement), false))
            {
                overrideConditionType = ConditionType.SkillRequirement;
            }

            foreach (var attribute in element.Attributes())
            {
                if (!IsValid(attribute)) { continue; }
                if (predicate != null && !predicate(attribute)) { continue; }

                var (comparisonOperator, attributeValueString) = ExtractComparisonOperatorFromConditionString(attribute.Value);
                if (string.IsNullOrWhiteSpace(attributeValueString))
                {
                    DebugConsole.ThrowError($"Conditional attribute value is empty: {element}");
                    continue;
                }

                var conditionType = overrideConditionType ??
                    (Enum.TryParse(attribute.Name.LocalName, ignoreCase: true, out ConditionType type)
                        ? type
                        : ConditionType.PropertyValueOrAffliction);

                yield return new PropertyConditional(
                    attributeName: attribute.NameAsIdentifier(),
                    comparisonOperator: comparisonOperator,
                    attributeValue: attributeValueString,
                    targetItemComponent: targetItemComponent,
                    targetSelf: targetSelf,
                    targetContainer: targetContainer,
                    targetGrandParent: targetGrandParent,
                    targetContainedItem: targetContainedItem,
                    conditionType: conditionType);
            }
        }

        private static bool IsValid(XAttribute attribute)
        {
            switch (attribute.Name.ToString().ToLowerInvariant())
            {
                case "targetitemcomponent":
                case "targetself":
                case "targetcontainer":
                case "targetgrandparent":
                case "targetcontaineditem":
                case "skillrequirement":
                case "targetslot":
                    return false;
                default:
                    return true;
            }
        }

        private PropertyConditional(
            Identifier attributeName,
            ComparisonOperatorType comparisonOperator,
            string attributeValue,
            string targetItemComponent,
            bool targetSelf,
            bool targetContainer,
            bool targetGrandParent,
            bool targetContainedItem,
            ConditionType conditionType)
        {
            AttributeName = attributeName;

            TargetItemComponent = targetItemComponent;
            TargetSelf = targetSelf;
            TargetContainer = targetContainer;
            TargetGrandParent = targetGrandParent;
            TargetContainedItem = targetContainedItem;

            Type = conditionType;

            ComparisonOperator = comparisonOperator;
            AttributeValue = attributeValue;
            AttributeValueAsTags = AttributeValue.Split(',')
                //, options: StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToIdentifier())
                .ToImmutableArray();
            if (float.TryParse(AttributeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                FloatValue = value;
            }
        }

        public static (ComparisonOperatorType ComparisonOperator, string ConditionStr) ExtractComparisonOperatorFromConditionString(string str)
        {
            str ??= "";

            ComparisonOperatorType op = ComparisonOperatorType.Equals;
            string conditionStr = str;
            if (str.IndexOf(' ') is var i and >= 0)
            {
                op = GetComparisonOperatorType(str[..i]);
                if (op != ComparisonOperatorType.None) { conditionStr = str[(i + 1)..]; }
                else { op = ComparisonOperatorType.Equals; }
            }
            return (op, conditionStr);
        }

        public static ComparisonOperatorType GetComparisonOperatorType(string op)
        {
            //thanks xml for not letting me use < or > in attributes :(
            switch (op.ToLowerInvariant())
            {
                case "e":
                case "eq":
                case "equals":
                    return ComparisonOperatorType.Equals;
                case "ne":
                case "neq":
                case "notequals":
                case "!":
                case "!e":
                case "!eq":
                case "!equals":
                    return ComparisonOperatorType.NotEquals;
                case "gt":
                case "greaterthan":
                    return ComparisonOperatorType.GreaterThan;
                case "lt":
                case "lessthan":
                    return ComparisonOperatorType.LessThan;
                case "gte":
                case "gteq":
                case "greaterthanequals":
                    return ComparisonOperatorType.GreaterThanEquals;
                case "lte":
                case "lteq":
                case "lessthanequals":
                    return ComparisonOperatorType.LessThanEquals;
                default:
                    return ComparisonOperatorType.None;
            }
        }

        private bool ComparisonOperatorIsNotEquals => ComparisonOperator == ComparisonOperatorType.NotEquals;

        public bool Matches(ISerializableEntity? target)
        {
            return TargetContainedItem
                ? MatchesContained(target)
                : MatchesDirect(target);
        }

        private bool MatchesContained(ISerializableEntity? target)
        {
            var containedItems = target switch
            {
                Item item
                    => item.ContainedItems,
                ItemComponent ic
                    => ic.Item.ContainedItems,
                Character {Inventory: { } characterInventory}
                    => characterInventory.AllItems,
                _
                    => Enumerable.Empty<Item>()
            };
            foreach (var containedItem in containedItems)
            {
                if (MatchesDirect(containedItem)) { return true; }
            }
            return false;
        }

        private bool MatchesDirect(ISerializableEntity? target)
        {
            Character? targetChar = target as Character;
            if (target is Limb limb) { targetChar = limb.character; }
            switch (Type)
            {
                case ConditionType.PropertyValueOrAffliction:
                    // First try checking for a property belonging to the target
                    if (target?.SerializableProperties != null
                        && target.SerializableProperties.TryGetValue(AttributeName, out var property))
                    {
                        return PropertyMatchesRequirement(target, property);
                    }
                    // Then try checking for an affliction affecting the target
                    if (targetChar is { CharacterHealth: { } health })
                    {
                        var affliction = health.GetAffliction(AttributeName.ToIdentifier());
                        float afflictionStrength = affliction?.Strength ?? 0f;

                        return NumberMatchesRequirement(afflictionStrength);
                    }
                    return ComparisonOperatorIsNotEquals;
                case ConditionType.SkillRequirement:
                    if (targetChar != null)
                    {
                        float skillLevel = targetChar.GetSkillLevel(AttributeName.ToIdentifier());

                        return NumberMatchesRequirement(skillLevel);
                    }
                    return ComparisonOperatorIsNotEquals;
                case ConditionType.HasTag:
                    return ItemMatchesTagCondition(target);
                case ConditionType.HasStatusTag:
                    if (target == null) { return ComparisonOperatorIsNotEquals; }

                    // TODO: revisit this. As written, the current behavior is:
                    // - ComparisonOperatorType.Equals: true when any effects have all tags
                    // - ComparisonOperatorType.NotEquals: true when none of the effects have any of the tags
                    int matches = 0;

                    foreach (var durationEffect in StatusEffect.DurationList)
                    {
                        if (!durationEffect.Targets.Contains(target)) { continue; }
                        if (StatusEffectMatchesTagCondition(durationEffect.Parent)) { matches++; }
                    }

                    foreach (var delayedEffect in DelayedEffect.DelayList)
                    {
                        if (!delayedEffect.Targets.Contains(target)) { continue; }
                        if (StatusEffectMatchesTagCondition(delayedEffect.Parent)) { matches++; }
                    }

                    return ComparisonOperatorIsNotEquals
                        ? matches >= StatusEffect.DurationList.Count + DelayedEffect.DelayList.Count
                        : matches > 0;
                default:
                    bool equals = CheckOnlyEquality(target);
                    return ComparisonOperatorIsNotEquals
                        ? !equals
                        : equals;
            }
        }

        private bool CheckOnlyEquality(ISerializableEntity? target)
        {
            switch (Type)
            {
                case ConditionType.Name:
                    if (target == null) { return false; }

                    return target.Name == AttributeValue;
                case ConditionType.HasSpecifierTag:
                {
                    if (target is not Character {Info: { } characterInfo})
                    {
                        return false;
                    }

                    return AttributeValueAsTags.All(characterInfo.Head.Preset.TagSet.Contains);
                }
                case ConditionType.SpeciesName:
                {
                    if (target is not Character targetCharacter)
                    {
                        return false;
                    }

                    return targetCharacter.SpeciesName == AttributeValue;
                }
                case ConditionType.SpeciesGroup:
                {
                    if (target is not Character targetCharacter)
                    {
                        return false;
                    }

                    return CharacterParams.CompareGroup(AttributeValue.ToIdentifier(), targetCharacter.Params.Group);
                }
                case ConditionType.EntityType:
                    return AttributeValue.ToLowerInvariant() switch
                    {
                        "character"
                            => target is Character,
                        "limb"
                            => target is Limb,
                        "item"
                            => target is Item,
                        "structure"
                            => target is Structure,
                        "null"
                            => target == null,
                        _
                            => false
                    };
                case ConditionType.LimbType:
                {
                    return target is Limb limb
                           && Enum.TryParse(AttributeValue, ignoreCase: true, out LimbType attributeLimbType)
                           && attributeLimbType == limb.type;
                }
            }
            return false;
        }

        private bool SufficientTagMatches(int matches)
        {
            return ComparisonOperatorIsNotEquals
                ? matches <= 0
                : matches >= AttributeValueAsTags.Length;
        }

        private bool ItemMatchesTagCondition(ISerializableEntity? target)
        {
            if (target is not Item item) { return ComparisonOperatorIsNotEquals; }

            int matches = 0;
            foreach (var tag in AttributeValueAsTags)
            {
                if (item.HasTag(tag)) { matches++; }
            }
            return SufficientTagMatches(matches);
        }

        public bool TargetTagMatchesTagCondition(Identifier targetTag)
        {
            if (targetTag.IsEmpty || Type != ConditionType.HasTag) { return false; }

            int matches = 0;
            foreach (var tag in AttributeValueAsTags)
            {
                if (targetTag == tag) { matches++; }
            }
            return SufficientTagMatches(matches);
        }

        private bool StatusEffectMatchesTagCondition(StatusEffect statusEffect)
        {
            int matches = 0;
            foreach (var tag in AttributeValueAsTags)
            {
                if (statusEffect.HasTag(tag.Value)) { matches++; }
            }
            return SufficientTagMatches(matches);
        }

        private bool NumberMatchesRequirement(float testedValue)
        {
            if (!FloatValue.HasValue) { return ComparisonOperatorIsNotEquals; }
            float value = FloatValue.Value;

            return ComparisonOperator switch
            {
                ComparisonOperatorType.Equals
                    => MathUtils.NearlyEqual(testedValue, value),
                ComparisonOperatorType.NotEquals
                    => !MathUtils.NearlyEqual(testedValue, value),
                ComparisonOperatorType.GreaterThan
                    => testedValue > value,
                ComparisonOperatorType.GreaterThanEquals
                    => testedValue >= value,
                ComparisonOperatorType.LessThan
                    => testedValue < value,
                ComparisonOperatorType.LessThanEquals
                    => testedValue <= value,
                _
                    => false
            };
        }

        private bool PropertyMatchesRequirement(ISerializableEntity target, SerializableProperty property)
        {
            Type type = property.PropertyType;

            if (type == typeof(float) || type == typeof(int))
            {
                float floatValue = property.GetFloatValue(target);
                return NumberMatchesRequirement(floatValue);
            }

            switch (ComparisonOperator)
            {
                case ComparisonOperatorType.Equals:
                case ComparisonOperatorType.NotEquals:
                    bool equals;
                    if (type == typeof(bool))
                    {
                        bool attributeValueBool = AttributeValue.IsTrueString();
                        equals = property.GetBoolValue(target) == attributeValueBool;
                    }
                    else
                    {
                        var value = property.GetValue(target);
                        equals = AreValuesEquivalent(value, AttributeValue);
                    }

                    return ComparisonOperatorIsNotEquals
                        ? !equals
                        : equals;
                default:
                    DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                        + "Make sure the type of the value set in the config files matches the type of the property.");
                    return false;
            }

            static bool AreValuesEquivalent(object? value, string desiredValue)
            {
                if (value == null)
                {
                    return desiredValue.Equals("null", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    return (value.ToString() ?? "").Equals(desiredValue);
                }
            }
        }
    }

}
