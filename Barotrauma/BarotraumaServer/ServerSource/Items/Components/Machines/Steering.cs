using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        // TODO: an enumeration would be much cleaner
        public bool MaintainPos;
        public bool LevelStartSelected;
        public bool LevelEndSelected;

        public bool UnsentChanges
        {
            get { return unsentChanges; }
            set { unsentChanges = value; }
        }


        public void ServerRead(ClientNetObject type, IReadMessage msg, Barotrauma.Networking.Client c)
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

            if (!item.CanClientAccess(c)) return;

            user = c.Character;
            AutoPilot = autoPilot;

            if (dockingButtonClicked)
            {
                item.SendSignal(0, "1", "toggle_docking", sender: null);
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), true });
            }

            if (!AutoPilot)
            {
                steeringInput = newSteeringInput;
                steeringAdjustSpeed = MathHelper.Lerp(0.2f, 1.0f, c.Character.GetSkillLevel("helm") / 100.0f);
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

        public void ServerWrite(IWriteMessage msg, Barotrauma.Networking.Client c, object[] extraData = null)
        {
            msg.Write(autoPilot);
            msg.Write(extraData.Length > 2 && extraData[2] is bool && (bool)extraData[2]);

            if (!autoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.Write(steeringInput.X);
                msg.Write(steeringInput.Y);
                msg.Write(targetVelocity.X);
                msg.Write(targetVelocity.Y);
                msg.Write(steeringAdjustSpeed);
            }
            else
            {
                msg.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.Write(((Vector2)posToMaintain).X);
                    msg.Write(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.Write(LevelStartSelected);
                }
            }
        }
    }
}
