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
        
        public SetDataAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        [Serialize(OperationType.Set, IsPropertySaveable.Yes)]
        public OperationType Operation { get; set; }

        [Serialize(null, IsPropertySaveable.Yes)]
        public string Value { get; set; } = null!;

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; }

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
                object xmlValue = ConvertXMLValue(Value);
                PerformOperation(campaign.CampaignMetadata, Identifier, xmlValue, Operation);
            }

            isFinished = true;
        }

        public static void PerformOperation(CampaignMetadata metadata, Identifier identifier, object value, OperationType operation)
        {
            if (metadata == null) { return; }

            object currentValue = metadata.GetValue(identifier);

            float? originalValue = ConvertValueToFloat(currentValue ?? 0);
            float? newValue = ConvertValueToFloat(value);

            if ((originalValue == null || newValue == null) && operation != OperationType.Set)
            {
                DebugConsole.ThrowError($"Tried to perform numeric operations to a non number via SetDataAction (Existing: {currentValue?.GetType()}, New: {value.GetType()})");
                return;
            }

            switch (operation)
            {
                case OperationType.Set:
                    metadata.SetValue(identifier, value);
                    break;
                case OperationType.Add:
                    metadata.SetValue(identifier, originalValue + newValue ?? 0);
                    break;
                case OperationType.Multiply:
                    metadata.SetValue(identifier, originalValue * newValue ?? 0);
                    break;
            }
        }

        private static float? ConvertValueToFloat(object value)
        {
            if (value is float || value is int)
            {
                return (float?) Convert.ChangeType(value, typeof(float));
            }

            return null;
        }

        public static object ConvertXMLValue(string value)
        {
            if (bool.TryParse(value, out bool b))
            {
                return b;
            }
            
            if (float.TryParse(value, out float f))
            {
                return f;
            }

            return value;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetDataAction)} -> (Identifier: {Identifier.ColorizeObject()}, Value: {ConvertXMLValue(Value).ColorizeObject()}, Operation: {Operation.ColorizeObject()})";
        }
    }
}