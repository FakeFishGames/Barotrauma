using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class SetDataAction : EventAction
    {
        public enum OperationType
        {
            Set,
            Multiply,
            Add
        }
        
        public SetDataAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        [Serialize(OperationType.Set, true)]
        public OperationType Operation { get; set; }

        [Serialize(null, true)]
        public string Value { get; set; } = null!;

        [Serialize("", true)]
        public string Identifier { get; set; }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                object currentValue = campaign.CampaignMetadata.GetValue(Identifier);
                object xmlValue = ConvertXMLValue();

                float? originalValue = ConvertValueToFloat(currentValue ?? 0);
                float? newValue = ConvertValueToFloat(xmlValue);

                if ((originalValue == null || newValue == null) && Operation != OperationType.Set)
                {
                    DebugConsole.ThrowError($"Tried to perform numeric operations to a non number via SetDataAction (Existing: {currentValue?.GetType()}, New: {xmlValue.GetType()})");
                    return;
                }

                if (Identifier != null)
                {
                    switch (Operation)
                    {
                        case OperationType.Set:
                            campaign.CampaignMetadata.SetValue(Identifier, xmlValue);
                            break;
                        case OperationType.Add:
                            campaign.CampaignMetadata.SetValue(Identifier, originalValue + newValue ?? 0);
                            break;
                        case OperationType.Multiply:
                            campaign.CampaignMetadata.SetValue(Identifier, originalValue * newValue ?? 0);
                            break;
                    }
                }
            }

            isFinished = true;
        }

        private static float? ConvertValueToFloat(object value)
        {
            if (value is float || value is int)
            {
                return (float?) Convert.ChangeType(value, typeof(float));
            }

            return null;
        }

        private object ConvertXMLValue()
        {
            if (bool.TryParse(Value, out bool b))
            {
                return b;
            }
            
            if (float.TryParse(Value, out float f))
            {
                return f;
            }

            return Value;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetDataAction)} -> (Identifier: {Identifier.ColorizeObject()}, Value: {ConvertXMLValue().ColorizeObject()}, Operation: {Operation.ColorizeObject()})";
        }
    }
}