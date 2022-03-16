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
            
            msg.Write(launch);
            if (launch)
            {
                msg.Write(User.ID);
                msg.Write(launchPos.X);
                msg.Write(launchPos.Y);
                msg.Write(launchRot);
            }

            bool stuck = StickTarget != null && !item.Removed && !StickTargetRemoved();
            msg.Write(stuck);
            if (stuck)
            {
                msg.Write(item.Submarine?.ID ?? Entity.NullEntityID);
                msg.Write(item.CurrentHull?.ID ?? Entity.NullEntityID);
                msg.Write(item.SimPosition.X);
                msg.Write(item.SimPosition.Y);
                msg.Write(stickJoint.Axis.X);
                msg.Write(stickJoint.Axis.Y);
                if (StickTarget.UserData is Structure structure)
                {
                    msg.Write(structure.ID);
                    int bodyIndex = structure.Bodies.IndexOf(StickTarget);
                    msg.Write((byte)(bodyIndex == -1 ? 0 : bodyIndex));
                }
                else if (StickTarget.UserData is Entity entity)
                {
                    msg.Write(entity.ID);
                }
                else if (StickTarget.UserData is Limb limb)
                {
                    msg.Write(limb.character.ID);
                    msg.Write((byte)Array.IndexOf(limb.character.AnimController.Limbs, limb));
                }
                else
                {
                    throw new NotImplementedException(StickTarget.UserData?.ToString() ?? "null" + " is not a valid projectile stick target.");
                }
            }
        }
    }
}
