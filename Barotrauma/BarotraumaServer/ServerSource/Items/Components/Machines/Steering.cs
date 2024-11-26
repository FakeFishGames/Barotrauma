﻿using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private readonly struct EventData : IEventData
        {
            public readonly bool DockingButtonClicked;
            
            public EventData(bool dockingButtonClicked)
            {
                DockingButtonClicked = dockingButtonClicked;
            }
        }
        
        // TODO: an enumeration would be much cleaner
        public bool MaintainPos;
        public bool LevelStartSelected;
        public bool LevelEndSelected;

        public bool UnsentChanges
        {
            get { return unsentChanges; }
            set { unsentChanges = value; }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            pathFinder = null;
        }


        public void ServerEventRead(IReadMessage msg, Client c)
        {
            bool autoPilot = msg.ReadBoolean();
            bool dockingButtonClicked = msg.ReadBoolean();
            Vector2 newSteeringInput = targetVelocity;
            Vector2? newPosToMaintain = null;
            bool headingToStart = false;

            if (autoPilot)
            {
                bool maintainPos = msg.ReadBoolean();
                if (maintainPos)
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadSingle(),
                        msg.ReadSingle());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newSteeringInput = new Vector2(msg.ReadSingle(), msg.ReadSingle());
            }

            if (!item.CanClientAccess(c)) { return; }

            user = c.Character;
            AutoPilot = autoPilot;

            if (dockingButtonClicked)
            {
                item.SendSignal(new Signal("1", sender: c.Character), "toggle_docking");
                item.CreateServerEvent(this, new EventData(dockingButtonClicked: true));
            }

            if (!AutoPilot)
            {
                steeringInput = newSteeringInput;
                steeringAdjustSpeed = MathHelper.Lerp(0.2f, 1.0f, c.Character.GetSkillLevel(Tags.HelmSkill) / 100.0f);
            }
            else
            {
                MaintainPos = newPosToMaintain != null;
                posToMaintain = newPosToMaintain;

                if (posToMaintain == null)
                {
                    LevelStartSelected = headingToStart;
                    LevelEndSelected = !headingToStart;
                    UpdatePath();
                }
                else
                {
                    LevelStartSelected = false;
                    LevelEndSelected = false;
                }
            }

            //notify all clients of the changed state
            unsentChanges = true;
        }

        public void ServerEventWrite(IWriteMessage msg, Barotrauma.Networking.Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(autoPilot);
            msg.WriteBoolean(TryExtractEventData<EventData>(extraData, out var eventData) && eventData.DockingButtonClicked);
            msg.WriteUInt16(user?.ID ?? Entity.NullEntityID);

            if (!autoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.WriteSingle(steeringInput.X);
                msg.WriteSingle(steeringInput.Y);
                msg.WriteSingle(targetVelocity.X);
                msg.WriteSingle(targetVelocity.Y);
                msg.WriteSingle(steeringAdjustSpeed);
            }
            else
            {
                msg.WriteBoolean(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.WriteSingle(((Vector2)posToMaintain).X);
                    msg.WriteSingle(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.WriteBoolean(LevelStartSelected);
                }
            }
        }
    }
}
