using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    public partial class Submarine
    {
        public readonly struct SetLayerEnabledEventData : NetEntityEvent.IData
        {
            public readonly Identifier Layer;
            public readonly bool Enabled;
            public readonly float ShakeAmount;
            public readonly Vector2 Acceleration;

            public SetLayerEnabledEventData(Identifier layer, bool enabled, float shakeAmount, Vector2 acceleration)
            {
                Layer = layer;
                Enabled = enabled;
                ShakeAmount = shakeAmount;
                Acceleration = acceleration;
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
                msg.WriteSingle(setLayerEnabledEventData.ShakeAmount);
                msg.WriteSingle(setLayerEnabledEventData.Acceleration.X);
                msg.WriteSingle(setLayerEnabledEventData.Acceleration.Y);
            }
            else
            {
                throw new Exception($"Error while writing a network event for the submarine \"{Info.Name} ({ID})\". Unrecognized event data: {extraData?.GetType().Name ?? "null"}");
            }
        }
    }
}
