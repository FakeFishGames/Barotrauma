using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RelayComponent : PowerTransfer, IServerSerializable
    {
        private float maxPower;
        
        [Editable, HasDefaultValue(1000.0f, true)]
        public float MaxPower
        {
            get { return maxPower; }
            set
            {
                maxPower = Math.Max(0.0f, value);
            }
        }

        private bool isOn;

        [Editable, HasDefaultValue(false, true)]
        public bool IsOn
        {
            get
            {
                return IsActive;
            }
            set
            {
                isOn = value;
                IsActive = value;
                if (!IsActive)
                {
                    currPowerConsumption = 0.0f;
                }
            }
        }

        public RelayComponent(Item item, XElement element)
            : base (item, element)
        {
            IsActive = isOn;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            item.SendSignal(0, IsOn ? "1" : "0", "state_out", null);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            if (connection.IsPower && connection.Name.Contains("_out")) return;

            if (item.Condition <= 0.0f) return;

            if (power > maxPower) item.Condition = 0.0f;
            
            if (connection.Name.Contains("_in"))
            {
                if (!IsOn) return;

                string outConnection = connection.Name.Contains("power_in") ? "power_out" : "signal_out";

                int connectionNumber = -1;
                int.TryParse(connection.Name.Substring(connection.Name.Length - 1, 1), out connectionNumber);

                if (connectionNumber > 0) outConnection += connectionNumber;

                item.SendSignal(stepsTaken, signal, outConnection, sender,  power);
            }
            else if (connection.Name == "toggle")
            {
                SetState(!IsOn, false);
            }
            else if (connection.Name == "set_state")
            {
                SetState(signal != "0", false);
            }
        }

        public void SetState(bool on, bool isNetworkMessage)
        {
            if (GameMain.Client != null && !isNetworkMessage) return;

            if (on != IsOn && GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }

            IsOn = on;
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(isOn);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            SetState(msg.ReadBoolean(), true);
        }
    }
}
