#nullable enable
using System;
using System.Linq;

namespace Barotrauma
{
    class CheckDataAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; } = Identifier.Empty;

        [Serialize("", IsPropertySaveable.Yes)]
        public string Condition { get; set; } = "";

        [Serialize(false, IsPropertySaveable.Yes, "Forces the comparison to use string instead of attempting to parse it as a boolean or a float first")]
        public bool ForceString { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, "Performs the comparison against a metadata by identifier instead of a constant value")]
        public bool CheckAgainstMetadata { get; set; }

        protected object? value2;
        protected object? value1;

        protected PropertyConditional.ComparisonOperatorType Operator { get; set; }

        public CheckDataAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
            if (string.IsNullOrEmpty(Condition))
            {
                Condition = element.GetAttributeString("value", string.Empty)!;
                if (string.IsNullOrEmpty(Condition))
                {
                    DebugConsole.ThrowError($"Error in scripted event \"{parentEvent.Prefab.Identifier}\". CheckDataAction with no condition set ({element}).");
                }
            }
        }

        public CheckDataAction(ContentXElement element, string parentDebugString) : base(null, element)
        {
            if (string.IsNullOrEmpty(Condition))
            {
                Condition = element.GetAttributeString("value", string.Empty)!;
                if (string.IsNullOrEmpty(Condition))
                {
                    DebugConsole.ThrowError($"Error in scripted event \"{parentDebugString}\". CheckDataAction with no condition set ({element}).");
                }
            }
        }

        public bool GetSuccess()
        {
            return DetermineSuccess() ?? false;
        }

        protected override bool? DetermineSuccess()
        {
            if (GameMain.GameSession?.GameMode is not CampaignMode campaignMode) { return false; }

            (Operator, string value) = PropertyConditional.ExtractComparisonOperatorFromConditionString(Condition);
            if (Operator == PropertyConditional.ComparisonOperatorType.None)
            {
                DebugConsole.ThrowError($"{Condition} is invalid, it should start with an operator followed by a boolean or a floating point value.");
                return false;
            }

            if (CheckAgainstMetadata)
            {
                object? metadata1 = campaignMode.CampaignMetadata.GetValue(Identifier);
                object? metadata2 = campaignMode.CampaignMetadata.GetValue(value.ToIdentifier());

                if (metadata1 == null || metadata2 == null)
                {
                    return Operator switch
                    {
                        PropertyConditional.ComparisonOperatorType.Equals => metadata1 == metadata2,
                        PropertyConditional.ComparisonOperatorType.NotEquals => metadata1 != metadata2,
                        _ => false
                    };
                }

                if (!ForceString)
                {
                    switch (metadata1)
                    {
                        case bool bool1 when metadata2 is bool bool2:
                            return CompareBool(bool1, bool2) ?? false;
                        case float float1 when metadata2 is float float2:
                            return CompareFloat(float1, float2) ?? false;
                    }
                }

                if (metadata1 is string string1 && metadata2 is string string2)
                {
                    return CompareString(string1, string2) ?? false;
                }

                return false;
            }

            if (!ForceString)
            {
                bool? tryBoolean = TryBoolean(campaignMode, value);
                if (tryBoolean != null) { return tryBoolean; }

                bool? tryFloat = TryFloat(campaignMode, value);
                if (tryFloat != null) { return tryFloat; }
            }

            bool? tryString = TryString(campaignMode, value);
            if (tryString != null) { return tryString; }

            return false;
        }

        private bool? TryBoolean(CampaignMode campaignMode, string value)
        {
            if (bool.TryParse(value, out bool b))
            {
                return CompareBool(GetBool(campaignMode), b);
            }

            DebugConsole.Log($"{value} != bool");
            return null;
        }

        private bool? CompareBool(bool val1, bool val2)
        {
            value1 = val1;
            value2 = val2;
            switch (Operator)
            {
                case PropertyConditional.ComparisonOperatorType.Equals:
                    return val1 == val2;
                case PropertyConditional.ComparisonOperatorType.NotEquals:
                    return val1 != val2;
                default:
                    DebugConsole.Log($"Only \"Equals\" and \"Not equals\" operators are allowed for a boolean (was {Operator} for {val2}).");
                    return false;
            }
        }

        private bool? TryFloat(CampaignMode campaignMode, string value)
        {
            if (float.TryParse(value, out float f))
            {
                return CompareFloat(GetFloat(campaignMode), f);
            }

            DebugConsole.Log($"{value} != float");
            return null;
        }

        private bool? CompareFloat(float val1, float val2)
        {
            value1 = val1;
            value2 = val2;
            switch (Operator)
            {
                case PropertyConditional.ComparisonOperatorType.Equals:
                    return MathUtils.NearlyEqual(val1, val2);
                case PropertyConditional.ComparisonOperatorType.GreaterThan:
                    return val1 > val2;
                case PropertyConditional.ComparisonOperatorType.GreaterThanEquals:
                    return val1 >= val2;
                case PropertyConditional.ComparisonOperatorType.LessThan:
                    return val1 < val2;
                case PropertyConditional.ComparisonOperatorType.LessThanEquals:
                    return val1 <= val2;
                case PropertyConditional.ComparisonOperatorType.NotEquals:
                    return !MathUtils.NearlyEqual(val1, val2);
            }

            return null;
        }

        private bool? TryString(CampaignMode campaignMode, string value)
        {
            return CompareString(GetString(campaignMode), value);
        }

        private bool? CompareString(string val1, string val2)
        {
            value1 = val1;
            value2 = val2;
            bool equals = string.Equals(val1, val2, StringComparison.OrdinalIgnoreCase);
            switch (Operator)
            { 
                case PropertyConditional.ComparisonOperatorType.Equals:
                    return equals;
                case PropertyConditional.ComparisonOperatorType.NotEquals:
                    return !equals;
                default:
                    DebugConsole.Log($"Only \"Equals\" and \"Not equals\" operators are allowed for a string (was {Operator} for {val2}).");
                    return null;
            }
        }

        protected virtual bool GetBool(CampaignMode campaignMode)
        {
            return campaignMode.CampaignMetadata.GetBoolean(Identifier);
        }
        
        protected virtual float GetFloat(CampaignMode campaignMode)
        {
            return campaignMode.CampaignMetadata.GetFloat(Identifier);
        }

        private string GetString(CampaignMode campaignMode)
        {
            return campaignMode.CampaignMetadata.GetString(Identifier);
        }

        public override string ToDebugString()
        {
            string condition = "?";
            if (value2 != null && value1 != null)
            {
                condition = $"{value1.ColorizeObject()} {Operator.ColorizeObject()} {value2.ColorizeObject()}";
            }

            return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckDataAction)} -> (Data: {Identifier.ColorizeObject()}, Success: {succeeded.ColorizeObject()}, Expression: {condition})";
        }
    }
}