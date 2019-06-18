using Barotrauma.Networking;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RelayComponent : PowerTransfer, IServerSerializable
    {
        private float maxPower;
        
        private bool isOn;

        [Editable, Serialize(1000.0f, true)]
        public float MaxPower
        {
            get { return maxPower; }
            set
            {
                maxPower = Math.Max(0.0f, value);
            }
        }
        
        [Editable, Serialize(false, true)]
        public bool IsOn
        {
            get
            {
                return isOn;
            }
            set
            {
                isOn = value;
                CanTransfer = value;
                if (!isOn)
                {
                    currPowerConsumption = 0.0f;
                }
            }
        }
        
        public RelayComponent(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            item.SendSignal(0, IsOn ? "1" : "0", "state_out", null);

            if (Math.Min(-currPowerConsumption, PowerLoad) > maxPower && CanBeOverloaded)
            {
                item.Condition = 0.0f;
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection.IsPower || item.Condition <= 0.0f) return;

            if (connection.Name.Contains("_in"))
            {
                if (!IsOn) return;

                string outConnection = connection.Name.Contains("power_in") ? "power_out" : "signal_out";

                int connectionNumber = -1;
                int.TryParse(connection.Name.Substring(connection.Name.Length - 1, 1), out connectionNumber);
                if (connectionNumber > 0) outConnection += connectionNumber;
                item.SendSignal(stepsTaken, signal, outConnection, sender, power, source, signalStrength);
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
#if CLIENT
            if (GameMain.Client != null && !isNetworkMessage) return;
#endif

#if SERVER
            if (on != IsOn && GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
#endif

            IsOn = on;
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(isOn);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            SetState(msg.ReadBoolean(), true);
        }
    }
}
