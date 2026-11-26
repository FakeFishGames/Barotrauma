using Barotrauma.Networking;
using System;
using System.Reflection;

namespace Barotrauma
{
    /// <summary>
    /// Sets a campaign metadata value. The metadata can be any arbitrary data you want to save: for example, whether some event has been completed, the number of times something has been done during the campaign, or at what stage of some multi-part event chain the crew is at.
    /// </summary>
    public class SetDataAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
    {
        [NetworkSerialize]
        public readonly record struct SetMetadataOperation(OperationType OperationType, string Value, Identifier Identifier) : INetSerializableStruct;

        public enum OperationType
        {
            Set,
            Multiply,
            Add
        }

        [Serialize(OperationType.Set, IsPropertySaveable.Yes, description: "Do you want to set the metadata to a specific value, multiply it, or add to it.")]
        public OperationType Operation { get; set; }

        [Serialize(null, IsPropertySaveable.Yes, description: "Depending on the operation, the value you want to set the metadata to, multiply it with, or add to it.")]
        public string Value { get; set; } = null!;

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the metadata to set. Can be any arbitrary identifier, e.g. itemscollected, my_custom_event_state, specialnpckilled...")]
        public Identifier Identifier { get; set; }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset(bool shouldEndConversations = true)
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

#if SERVER
                WriteSetMetadata();
#endif
            }

            isFinished = true;
        }

        public static void PerformOperation(CampaignMetadata metadata, SetMetadataOperation operation)
        {
            object xmlValue = ConvertXMLValue(operation.Value);
            PerformOperation(metadata, operation.Identifier, xmlValue, operation.OperationType);
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

#if SERVER
        private void WriteSetMetadata()
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                WriteSetMetadata(client);
            }
        }

        private void WriteSetMetadata(Client client)
        {
            var message = new WriteOnlyMessage();
            message.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            message.WriteByte((byte)EventManager.NetworkEventType.SETCAMPAIGNMETADATA);
            message.WriteNetSerializableStruct(new SetMetadataOperation(Operation, Value, Identifier));
            GameMain.Server.ServerPeer.Send(message, client.Connection, DeliveryMethod.Reliable);
        }
#endif

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetDataAction)} -> (Identifier: {Identifier.ColorizeObject()}, Value: {ConvertXMLValue(Value).ColorizeObject()}, Operation: {Operation.ColorizeObject()})";
        }
    }
}