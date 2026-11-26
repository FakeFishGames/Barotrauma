using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HomeAndHarbor
{
    /// <summary>
    /// An UI that can be used to rotate and launch a turret, or an item that uses the turret component to rotate (such as a searchlight or a thruster).
    /// </summary>
    public partial class TurretControlPanel : ItemComponent
    {
        private bool lastSentState;

        /// <summary>
        /// The character who's pressing the firing button. Only they can release it, so others rotating the knob don't stop the firing.
        /// </summary>
        private Character? firingCharacter;

        partial void UpdateProjSpecific(float deltaTime)
        {
            //notify clients if the firing state changed without a client telling us to (= the firing button timed out)
            //no need to send periodic "still pressing" updates here: the "still pressing" updates from the client are relayed in ServerEventRead
            if (lastSentState != sendFiringSignal)
            {
                item.CreateServerEvent(this);
                lastSentState = sendFiringSignal;
            }
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            float rotationDeg = msg.ReadSingle();
            bool buttonPressed = msg.ReadBoolean();
            bool rotationChanged = msg.ReadBoolean();

            if (item.CanClientAccess(c))
            {
                user = c.Character;
                //only apply the rotation if the client actually rotated the knob:
                //otherwise e.g. someone holding the firing button could override the rotation set by someone else with their outdated rotation
                if (rotationChanged)
                {
                    RotationDeg = rotationDeg;
                }
                if (buttonPressed)
                {
                    firingCharacter = c.Character;
                    sendFiringSignal = true;
                    //keep the firing button pressed for up to 0.5 seconds,
                    //during which the client should either say they're still pressing the button or that they have released it
                    firingButtonPressedTimer = 0.5f;
                }
                else if (firingCharacter == c.Character)
                {
                    firingCharacter = null;
                    sendFiringSignal = false;
                }
            }

            //notify all clients of the changed state
            item.CreateServerEvent(this);
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
        {
            msg.WriteSingle(rotationDeg);
            msg.WriteBoolean(sendFiringSignal);
        }
    }
}