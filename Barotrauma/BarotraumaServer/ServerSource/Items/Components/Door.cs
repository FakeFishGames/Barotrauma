using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class Door
    {
        private readonly struct EventData : IEventData
        {
            public readonly bool ForcedOpen;
            
            public EventData(bool forcedOpen)
            {
                ForcedOpen = forcedOpen;
            }
        }
        
        partial void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage, bool forcedOpen)
        {
            if (IsStuck || isOpen == open)
            {
                return;
            }
            isOpen = open;

            //opening a partially stuck door makes it less stuck
            if (isOpen) { stuck = MathHelper.Clamp(stuck - StuckReductionOnOpen, 0.0f, 100.0f); }

            if (sendNetworkMessage)
            {
                item.CreateServerEvent(this, new EventData(forcedOpen));
            }
        }

        public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            bool forcedOpen = TryExtractEventData<EventData>(extraData, out var eventData) && eventData.ForcedOpen;
            base.ServerEventWrite(msg, c, extraData);

            msg.Write(isOpen);
            msg.Write(isBroken);
            msg.Write(forcedOpen); //forced open
            msg.Write(isStuck);
            msg.Write(isJammed);
            msg.WriteRangedSingle(stuck, 0.0f, 100.0f, 8);
            msg.Write(lastUser == null ? (UInt16)0 : lastUser.ID);
        }
    }
}
