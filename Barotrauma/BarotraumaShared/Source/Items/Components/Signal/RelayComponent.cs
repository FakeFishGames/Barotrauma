using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RelayComponent : PowerTransfer, IServerSerializable
    {
        private float maxPower;
        
        private bool isOn;

        private static readonly Dictionary<string, string> connectionPairs = new Dictionary<string, string>
        {
            { "power_in", "power_out"},
            { "signal_in", "signal_out" },
            { "signal_in1", "signal_out1" },
            { "signal_in2", "signal_out2" },
            { "signal_in3", "signal_out3" },
            { "signal_in4", "signal_out4" },
            { "signal_in5", "signal_out5" }
        };

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
            if (connection.IsPower || item.Condition <= 0.0f) { return; }

            if (connectionPairs.TryGetValue(connection.Name, out string outConnection))
            {
                if (!IsOn) { return; }
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
