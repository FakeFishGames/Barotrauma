using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RelayComponent : PowerTransfer, IServerSerializable
    {
        private float maxPower;

        private bool isOn;

        private float throttlePowerOutput;

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
        public float DisplayLoad { get; set; }

        [Editable, Serialize(1000.0f, true, description: "The maximum amount of power that can pass through the item.")]
        public float MaxPower
        {
            get { return maxPower; }
            set
            {
                maxPower = Math.Max(0.0f, value);
            }
        }

        [Editable, Serialize(false, true, description: "Can the relay currently pass power and signals through it.")]
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
            : base(item, element)
        {
            IsActive = true;
            throttlePowerOutput = MaxPower;
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            var connections = Item.Connections;
            if (connections != null)
            {
                foreach (KeyValuePair<string, string> connectionPair in connectionPairs)
                {
                    if (connections.Any(c => c.Name == connectionPair.Key) && !connections.Any(c => c.Name == connectionPair.Value))
                    {
                        DebugConsole.ThrowError("Error in item \"" + Name + "\" - matching connection pair not found for the connection \"" + connectionPair.Key + "\" (expecting \"" + connectionPair.Value + "\").");
                    }
                    else if (connections.Any(c => c.Name == connectionPair.Value) && !connections.Any(c => c.Name == connectionPair.Key))
                    {
                        DebugConsole.ThrowError("Error in item \"" + Name + "\" - matching connection pair not found for the connection \"" + connectionPair.Value + "\" (expecting \"" + connectionPair.Key + "\").");
                    }
                }
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            RefreshConnections();

            item.SendSignal(0, IsOn ? "1" : "0", "state_out", null);
			
            if (!CanTransfer) { Voltage = 0.0f; return; }

            if (isBroken)
            {
                SetAllConnectionsDirty();
                isBroken = false;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerOut != null)
            {
				bool overloaded = false;
                foreach (Connection recipient in powerOut.Recipients)
                {
                    var pt = recipient.Item.GetComponent<PowerTransfer>();
                    if (pt != null)
                    {
						float overload = -pt.CurrPowerConsumption - pt.PowerLoad;
						throttlePowerOutput += overload * deltaTime * 0.5f;
						overloaded = overload > 1.0f;
                    }
                }
                throttlePowerOutput = overloaded ?
					MathHelper.Clamp(throttlePowerOutput, 0.0f, MaxPower): 					
					Math.Max(throttlePowerOutput - MaxPower * 0.1f * deltaTime, 0.0f);
            }

            if (Math.Min(-currPowerConsumption, PowerLoad) > maxPower && CanBeOverloaded)
            {
                item.Condition = 0.0f;
            }
        }

        public override void ReceivePowerProbeSignal(Connection connection, Item source, float power)
        {
            if (!IsOn) { return; }

            //we've already received this signal
            if (lastPowerProbeRecipients.Contains(this)) { return; }
            lastPowerProbeRecipients.Add(this);

            if (power < 0.0f)
            {
                if (!connection.IsOutput || powerIn == null) { return; }

                //power being drawn from the power_out connection
                DisplayLoad -= Math.Min(power, 0.0f);
                powerLoad -= Math.Min(power + throttlePowerOutput, 0.0f);

                //pass the load to items connected to the input
                powerIn.SendPowerProbeSignal(source, Math.Max(power, -MaxPower));
            }
            else
            {
                if (connection.IsOutput || powerOut == null) { return; }
                //power being supplied to the power_in connection
                if (currPowerConsumption - power < -MaxPower)
                {
                    power += MaxPower + (currPowerConsumption - power);
                }

                currPowerConsumption -= power;

                foreach (Connection recipient in powerOut.Recipients)
                {
                    if (!recipient.IsPower) { continue; }
                    var powered = recipient.Item.GetComponent<Powered>();
                    if (powered == null) { continue; }
					
					float load = powered.CurrPowerConsumption;
					var powerTransfer = powered as PowerTransfer;
					if (powerTransfer != null) { load = powerTransfer.PowerLoad;  }

                    float powerOut = power * (load / Math.Max(powerLoad + throttlePowerOutput, 0.01f));
                    powered.ReceivePowerProbeSignal(recipient, source, Math.Min(powerOut, power));
                }
            }
            
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (item.Condition <= 0.0f || connection.IsPower) { return; }

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
