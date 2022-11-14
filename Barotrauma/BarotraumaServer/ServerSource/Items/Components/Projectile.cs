using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class Projectile : ItemComponent
    {
        private readonly struct EventData : IEventData
        {
            public readonly bool Launch;
            
            public EventData(bool launch)
            {
                Launch = launch;
            }
        }
        
        private float launchRot;

        public override bool ValidateEventData(NetEntityEvent.IData data)
            => TryExtractEventData<EventData>(data, out _);

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            var eventData = ExtractEventData<EventData>(extraData);
            bool launch = eventData.Launch;
            
            msg.WriteBoolean(launch);
            if (launch)
            {
                msg.WriteUInt16(User?.ID ?? 0);
                msg.WriteSingle(launchPos.X);
                msg.WriteSingle(launchPos.Y);
                msg.WriteSingle(launchRot);
            }

            bool stuck = StickTarget != null && !item.Removed && !StickTargetRemoved();
            msg.WriteBoolean(stuck);
            if (stuck)
            {
                msg.WriteUInt16(item.Submarine?.ID ?? Entity.NullEntityID);
                msg.WriteUInt16(item.CurrentHull?.ID ?? Entity.NullEntityID);
                msg.WriteSingle(item.SimPosition.X);
                msg.WriteSingle(item.SimPosition.Y);
                msg.WriteSingle(jointAxis.X);
                msg.WriteSingle(jointAxis.Y);
                if (StickTarget.UserData is Structure structure)
                {
                    msg.WriteUInt16(structure.ID);
                    int bodyIndex = structure.Bodies.IndexOf(StickTarget);
                    msg.WriteByte((byte)(bodyIndex == -1 ? 0 : bodyIndex));
                }
                else if (StickTarget.UserData is Entity entity)
                {
                    msg.WriteUInt16(entity.ID);
                }
                else if (StickTarget.UserData is Limb limb)
                {
                    msg.WriteUInt16(limb.character.ID);
                    msg.WriteByte((byte)Array.IndexOf(limb.character.AnimController.Limbs, limb));
                }
                else
                {
                    throw new NotImplementedException(StickTarget.UserData?.ToString() ?? "null" + " is not a valid projectile stick target.");
                }
            }
        }
    }
}
