#nullable enable
using System.Xml.Linq;

namespace Barotrauma
{
    class CheckDataAction : BinaryOptionAction
    {
        [Serialize("", true)]
        public string Identifier { get; set; } = null!;

        [Serialize("", true)]
        public string Condition { get; set; } = null!;

        protected object? value2;
        protected object? value1;

        protected PropertyConditional.OperatorType Operator { get; set; }

        public CheckDataAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            if (!(GameMain.GameSession?.GameMode is CampaignMode campaignMode)) { return false; }

            string[] splitString = Condition.Split(' ');
            string value = Condition;
            if (splitString.Length > 0)
            {
                for (int i = 1; i < splitString.Length; i++)
                {
                    value = splitString[i] + (i > 1 && i < splitString.Length ? " " : "");
                }
            }
            else
            {
                DebugConsole.ThrowError($"{Condition} is too short, it should start with an operator followed by a boolean or a floating point value.");
                return false;
            }

            string op = splitString[0];
            Operator = PropertyConditional.GetOperatorType(op);
            if (Operator == PropertyConditional.OperatorType.None) { return false; }

            bool? tryBoolean = TryBoolean(campaignMode, value);
            if (tryBoolean != null) { return tryBoolean; }

            bool? tryFloat = TryFloat(campaignMode, value);
            if (tryFloat != null) { return tryFloat; }

            DebugConsole.ThrowError($"{value2} ({Condition}) did not match a boolean or a float.");
            return false;
        }

        private bool? TryBoolean(CampaignMode campaignMode, string value)
        {
            if (bool.TryParse(value, out bool b))
            {
                bool target = GetBool(campaignMode);
                value1 = target;
                value2 = b;
                switch (Operator)
                {
                    case PropertyConditional.OperatorType.Equals:
                        return target == b;
                    case PropertyConditional.OperatorType.NotEquals:
                        return target != b;
                    default:
                        DebugConsole.Log($"Only \"Equals\" and \"Not equals\" operators are allowed for a boolean (was {Operator} for {value}).");
                        return false;
                }
            }

            DebugConsole.Log($"{value} != bool");
            return null;
        }

        private bool? TryFloat(CampaignMode campaignMode, string value)
        {
            if (float.TryParse(value, out float f))
            {
                float target = GetFloat(campaignMode);
                value1 = target;
                value2 = f;
                switch (Operator)
                {
                    case PropertyConditional.OperatorType.Equals:
                        return MathUtils.NearlyEqual(target, f);
                    case PropertyConditional.OperatorType.GreaterThan:
                        return target > f;
                    case PropertyConditional.OperatorType.GreaterThanEquals:
                        return target >= f;
                    case PropertyConditional.OperatorType.LessThan:
                        return target < f;
                    case PropertyConditional.OperatorType.LessThanEquals:
                        return target <= f;
                    case PropertyConditional.OperatorType.NotEquals:
                        return !MathUtils.NearlyEqual(target, f);
                }
            }

            DebugConsole.Log($"{value} != float");
            return null;
        }
        
        protected virtual bool GetBool(CampaignMode campaignMode)
        {
            return campaignMode.CampaignMetadata.GetBoolean(Identifier);
        }
        
        protected virtual float GetFloat(CampaignMode campaignMode)
        {
            return campaignMode.CampaignMetadata.GetFloat(Identifier);
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