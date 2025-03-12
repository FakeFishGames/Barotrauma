#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
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
            /// If there exists an affliction with an identifier that matches the given key, check against the strength of that affliction.
            /// Otherwise, check against the value of one of the target's properties.
            ///
            /// The target object's available properties depend on how that object is defined in the [source code](https://github.com/Regalis11/Barotrauma).
            ///
            /// This is not applicable if the element contains the attribute
            /// `SkillRequirement="true"`.
            /// </summary>
            /// <AutoDocEntryName value="Property or affliction identifier" />
            /// <example>
            /// <Conditional WatchersGaze="gt 0" /> <!-- there is an affliction with identifier WatchersGaze -> check for that affliction -->
            /// <Conditional IsDead="true" /> <!-- there is no affliction with identifier IsDead -> check property instead -->
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
            /// Check against the target's tags. Only works on items and characters.
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
            LimbType,

            /// <summary>
            /// Check against the current World Hostility setting (previously known as "Difficulty").
            /// </summary>
            WorldHostility,

            /// <summary>
            /// Check against the difficulty of the current level.
            /// </summary>
            LevelDifficulty
        }

        public enum LogicalOperatorType
        {
            And,
            Or
        }

        /// <summary>
        /// There are several ways to compare properties to values.
        /// The comparison operator to use can be specified by placing one of the following before the value to compare against.
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

        private readonly WorldHostilityOption cachedHostilityValue;

        /// <summary>
        /// If set to the name of one of the target's ItemComponents, the conditionals defined by this element check against the properties of that component.
        /// Only works on items.
        /// </summary>
        public readonly string TargetItemComponent;
        
        /// <summary>
        /// When targeting item components, should we require them all to match the conditional or any (default).
        /// </summary>
        public readonly LogicalOperatorType ItemComponentComparison;

        /// <summary>
        /// If set to true, the conditionals defined by this element check against the attacking character instead of the attacked character.
        /// Only applies to a character's attacks and targeting parameters.
        /// </summary>
        public readonly bool TargetSelf;

        /// <summary>
        /// If set to true, the conditionals defined by this element check against the entity containing the target.
        /// </summary>
        public readonly bool TargetContainer;

        /// <summary>
        /// If this and TargetContainer are set to true, the conditionals defined by this element check against the entity containing the target's container.
        /// For example, diving suits have a status effect that targets contained oxygen tanks, with a conditional that only passes if the locker containing the suit is powered.
        /// </summary>
        public readonly bool TargetGrandParent;

        /// <summary>
        /// If set to true, the conditionals defined by this element check against the items contained by the target. Only works with items.
        /// </summary>
        public readonly bool TargetContainedItem;

        public static IEnumerable<PropertyConditional> FromXElement(ContentXElement element, Predicate<XAttribute>? predicate = null)
        {
            string targetItemComponent = element.GetAttributeString(nameof(TargetItemComponent), string.Empty);
            bool targetContainer = element.GetAttributeBool(nameof(TargetContainer), false);
            bool targetSelf = element.GetAttributeBool(nameof(TargetSelf), false);
            bool targetGrandParent = element.GetAttributeBool(nameof(TargetGrandParent), false);
            bool targetContainedItem = element.GetAttributeBool(nameof(TargetContainedItem), false);
            
            LogicalOperatorType itemComponentComparison = element.GetAttributeEnum(nameof(ItemComponentComparison), LogicalOperatorType.Or);

            ConditionType? overrideConditionType = null;
            if (element.GetAttributeBool(nameof(ConditionType.SkillRequirement), false))
            {
                overrideConditionType = ConditionType.SkillRequirement;
            }

            foreach (var attribute in element.Attributes())
            {
                if (!IsValid(attribute)) { continue; }
                if (predicate != null && !predicate(attribute)) { continue; }

                (ComparisonOperatorType comparisonOperator, string attributeValueString) = ExtractComparisonOperatorFromConditionString(attribute.Value);
                if (string.IsNullOrWhiteSpace(attributeValueString))
                {
                    DebugConsole.ThrowError($"Conditional attribute value is empty: {element}", contentPackage: element.ContentPackage);
                    continue;
                }

                ConditionType conditionType = overrideConditionType ?? 
                                              (Enum.TryParse(attribute.Name.LocalName, ignoreCase: true, out ConditionType type) ? type : ConditionType.PropertyValueOrAffliction);

                yield return new PropertyConditional(
                    attributeName: attribute.NameAsIdentifier(),
                    comparisonOperator: comparisonOperator,
                    attributeValue: attributeValueString,
                    targetItemComponent: targetItemComponent,
                    itemComponentComparison: itemComponentComparison,
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
            LogicalOperatorType itemComponentComparison,
            bool targetSelf,
            bool targetContainer,
            bool targetGrandParent,
            bool targetContainedItem,
            ConditionType conditionType)
        {
            AttributeName = attributeName;

            TargetItemComponent = targetItemComponent;
            ItemComponentComparison = itemComponentComparison;
            TargetSelf = targetSelf;
            TargetContainer = targetContainer;
            TargetGrandParent = targetGrandParent;
            TargetContainedItem = targetContainedItem;

            Type = conditionType;

            ComparisonOperator = comparisonOperator;
            AttributeValue = attributeValue;
            AttributeValueAsTags = AttributeValue.Split(',')
                .Select(s => s.ToIdentifier())
                .ToImmutableArray();
            if (float.TryParse(AttributeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                FloatValue = value;
            }

            if (Type == ConditionType.WorldHostility && Enum.TryParse(AttributeValue, ignoreCase: true, out WorldHostilityOption hostilityValue))
            {
                cachedHostilityValue = hostilityValue;
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
                    // If an AfflictionPrefab with identifier AttributeName exists,
                    // check for an affliction affecting the target
                    if (AfflictionPrefab.Prefabs.ContainsKey(AttributeName))
                    {
                        if (targetChar is { CharacterHealth: { } health })
                        {
                            var affliction = health.GetAffliction(AttributeName);
                            float afflictionStrength = affliction?.Strength ?? 0f;

                            return NumberMatchesRequirement(afflictionStrength);
                        }
                    }
                    // Otherwise try checking for a property belonging to the target
                    else if (target?.SerializableProperties != null
                        && target.SerializableProperties.TryGetValue(AttributeName, out var property))
                    {
                        return PropertyMatchesRequirement(target, property);
                    }
                    else if (targetChar?.SerializableProperties != null
                        && targetChar.SerializableProperties.TryGetValue(AttributeName, out var characterProperty))
                    {
                        return PropertyMatchesRequirement(targetChar, characterProperty);
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
                    if (targetChar != null)
                    {
                        return CheckMatchingTags(targetChar.Params.HasTag);
                    }
                    if (target is Item item)
                    {
                        return CheckMatchingTags(item.HasTag);
                    }
                    return ComparisonOperatorIsNotEquals;
                case ConditionType.HasStatusTag:
                    if (target == null) { return ComparisonOperatorIsNotEquals; }

                    int numTagsFound = 0;
                    foreach (var tag in AttributeValueAsTags)
                    {
                        bool tagFound = false;
                        foreach (var durationEffect in StatusEffect.DurationList)
                        {
                            if (!durationEffect.Targets.Contains(target)) { continue; }
                            if (durationEffect.Parent.HasTag(tag))
                            {
                                tagFound = true;
                                break;
                            }
                        }
                        if (!tagFound)
                        {
                            foreach (var delayedEffect in DelayedEffect.DelayList)
                            {
                                if (!delayedEffect.Targets.Contains(target)) { continue; }
                                if (delayedEffect.Parent.HasTag(tag))
                                {
                                    tagFound = true;
                                    break;
                                }
                            }
                        }
                        if (tagFound)
                        {
                            numTagsFound++;
                        }
                    }
                    return ComparisonOperatorIsNotEquals
                        ? numTagsFound < AttributeValueAsTags.Length // true when some tag wasn't found
                        : numTagsFound >= AttributeValueAsTags.Length; // true when all the tags are found
                case ConditionType.LevelDifficulty:                    
                    if (Level.Loaded is { } level)
                    {
                        return NumberMatchesRequirement(level.Difficulty);
                    }
                    return false;                    
                case ConditionType.WorldHostility:                
                    if (GameMain.GameSession?.Campaign is CampaignMode campaign)
                    {
                        return Compare(campaign.Settings.WorldHostility, cachedHostilityValue, ComparisonOperator);
                    }
                    return false;                
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
                    if (target is Character targetCharacter)
                    {
                        return targetCharacter.SpeciesName == AttributeValue;
                    }
                    else if (target is Limb targetLimb)
                    {
                        return targetLimb.character.SpeciesName == AttributeValue;
                    }
                    return false;                    
                }
                case ConditionType.SpeciesGroup:
                    {
                        if (target is Character targetCharacter)
                        {
                            return CharacterParams.CompareGroup(AttributeValue.ToIdentifier(), targetCharacter.Params.Group);
                        }
                        else if (target is Limb targetLimb)
                        {
                            return CharacterParams.CompareGroup(AttributeValue.ToIdentifier(), targetLimb.character.Params.Group);
                        }
                        return false;
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
        
        private bool CheckMatchingTags(Func<Identifier, bool> predicate)
        {
            int matches = 0;
            foreach (Identifier tag in AttributeValueAsTags)
            {
                if (predicate(tag)) { matches++; }
            }
            return SufficientTagMatches(matches);
        }

        public bool TargetTagMatchesTagCondition(Identifier targetTag)
        {
            if (targetTag.IsEmpty || Type != ConditionType.HasTag) { return false; }
            return CheckMatchingTags(targetTag.Equals);
        }

        private bool NumberMatchesRequirement(float testedValue)
        {
            if (!FloatValue.HasValue) { return ComparisonOperatorIsNotEquals; }
            float value = FloatValue.Value;
            return CompareFloat(testedValue, value, ComparisonOperator);
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

        public static bool CompareFloat(float val1, float val2, ComparisonOperatorType op)
        {
            switch (op)
            {
                case ComparisonOperatorType.Equals:
                    return MathUtils.NearlyEqual(val1, val2);
                case ComparisonOperatorType.GreaterThan:
                    return val1 > val2;
                case ComparisonOperatorType.GreaterThanEquals:
                    return val1 >= val2;
                case ComparisonOperatorType.LessThan:
                    return val1 < val2;
                case ComparisonOperatorType.LessThanEquals:
                    return val1 <= val2;
                case ComparisonOperatorType.NotEquals:
                    return !MathUtils.NearlyEqual(val1, val2);
                default:
                    return false;
            }
        }

        public static bool Compare<T>(T leftValue, T rightValue, ComparisonOperatorType comparisonOperator) where T : IComparable
        {
            return comparisonOperator switch
            {
                ComparisonOperatorType.NotEquals => leftValue.CompareTo(rightValue) != 0,
                ComparisonOperatorType.GreaterThan => leftValue.CompareTo(rightValue) > 0,
                ComparisonOperatorType.LessThan => leftValue.CompareTo(rightValue) < 0,
                ComparisonOperatorType.GreaterThanEquals => leftValue.CompareTo(rightValue) >= 0,
                ComparisonOperatorType.LessThanEquals => leftValue.CompareTo(rightValue) <= 0,
                _ => leftValue.CompareTo(rightValue) == 0,
            };
        }
        
        /// <summary>
        /// Seeks for child elements of name "conditional" and bundles them with an attribute of name "comparison".
        /// </summary>
        public static LogicalComparison? LoadConditionals(ContentXElement element, LogicalOperatorType defaultOperatorType = LogicalOperatorType.And)
        {
            var conditionalElements = element.GetChildElements("conditional");
            if (conditionalElements.None()) { return default; }
            List<PropertyConditional> conditionals = new();
            foreach (ContentXElement subElement in conditionalElements)
            {
                conditionals.AddRange(FromXElement(subElement));
            }
            var logicalOperator = element.GetAttributeEnum("comparison", defaultOperatorType);
            return new LogicalComparison(conditionals, logicalOperator);
        }
        
        public static bool CheckConditionals(ISerializableEntity conditionalTarget, IEnumerable<PropertyConditional> conditionals, LogicalOperatorType logicalOperator)
        {
            if (conditionals == null) { return true; }
            if (conditionals.None()) { return true; }
            switch (logicalOperator)
            {
                case LogicalOperatorType.And:
                    foreach (var conditional in conditionals)
                    {
                        if (!conditional.Matches(conditionalTarget))
                        {
                            // Some conditional didn't match.
                            return false;
                        }
                    }
                    // All conditionals matched.
                    return true;
                case LogicalOperatorType.Or:
                    foreach (var conditional in conditionals)
                    {
                        if (conditional.Matches(conditionalTarget))
                        {
                            // Some conditional matched.
                            return true;
                        }
                    }
                    // None of the conditionals matched.
                    return false;
                default:
                    throw new NotSupportedException();
            }
        }
        
        /// <summary>
        /// Bundles up a bunch of conditionals with a logical operator.
        /// </summary>
        public class LogicalComparison
        {
            public readonly ImmutableArray<PropertyConditional> Conditionals;
            public readonly LogicalOperatorType LogicalOperator;
            
            public LogicalComparison(IEnumerable<PropertyConditional> conditionals, LogicalOperatorType logicalOperator)
            {
                Conditionals = conditionals.ToImmutableArray();
                LogicalOperator = logicalOperator;
            }
        }
    }
}
