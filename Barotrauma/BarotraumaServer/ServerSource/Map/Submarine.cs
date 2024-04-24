using Barotrauma.Networking;
using System;

namespace Barotrauma
{
    partial class Submarine
    {
        public readonly struct SetLayerEnabledEventData : NetEntityEvent.IData
        {
            public readonly Identifier Layer;
            public readonly bool Enabled;

            public SetLayerEnabledEventData(Identifier layer, bool enabled)
            {
                Layer = layer;
                Enabled = enabled;
            }
        }

        public void ServerWritePosition(ReadWriteMessage tempBuffer, Client c)
        {
            subBody.Body.ServerWrite(tempBuffer);
        }
        
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            if (extraData is SetLayerEnabledEventData setLayerEnabledEventData)
            {
                msg.WriteIdentifier(setLayerEnabledEventData.Layer);
                msg.WriteBoolean(setLayerEnabledEventData.Enabled);
            }
            else
            {
                throw new Exception($"Error while writing a network event for the submarine \"{Info.Name} ({ID})\". Unrecognized event data: {extraData?.GetType().Name ?? "null"}");
            }
        }
    }
}
