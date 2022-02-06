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

        private float prevVoltage;

        private float? newVoltage = null;

        private float prevLoad = 0;

        private float prevOutputLoad = 0;

        private float bufferdiff = 0;

        private float internalBuffer = 0;

        private float thirdInverseMax = 0;

        private float loadEqnConstant = 0;

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
        public float DisplayLoad
        {
            get
            {
                if (powerOut != null && powerOut.Grid != null)
                {
                    return powerOut.Grid.Load;
                }
                else
                {
                    return 0;
                }
            }
        }

        [Editable, Serialize(1000.0f, true, description: "The maximum amount of power that can pass through the item.")]
        public float MaxPower
        {
            get { return maxPower; }
            set
            {
                maxPower = Math.Max(0.0f, value);
                thirdInverseMax = 1 / (3 * maxPower);
                loadEqnConstant = (8 * maxPower ) / 3;
            }
        }

        [Editable, Serialize(true, true, description: "Can the relay currently pass power and signals through it.", alwaysUseInstanceValues: true)]
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
            prevVoltage = 0;
            internalBuffer = MaxPower * 2;

            // Set constants for load Formula to reduce calculation time
            thirdInverseMax = 1 / (3 * maxPower);
            loadEqnConstant = (8 * maxPower ) / 3;
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

            item.SendSignal(IsOn ? "1" : "0", "state_out");
            item.SendSignal(((int)Math.Round(-PowerLoad)).ToString(), "power_value_out");
            item.SendSignal(((int)Math.Round(DisplayLoad)).ToString(), "load_value_out");

            if (isBroken)
            {
                SetAllConnectionsDirty();
                isBroken = false;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (Voltage > OverloadVoltage && CanBeOverloaded)
            {
                item.Condition = 0.0f;
            }
        }

        /// <summary>
        /// Relay power consumption. Load consumption is based on the internal buffer
        /// this allows for the relay to react to demand and find equilibrium in loop configurations
        /// </summary>
        /// <param name="conn"></param>
        /// <returns></returns>
        public override float ConnCurrConsumption(Connection conn = null)
        {
            //Can't output or draw if broken
            if (isBroken)
            {
                return 0;
            }

            if (conn == powerIn)
            {
                float loadDraw = MaxPower;
                if (internalBuffer > MaxPower)
                {
                    //Buffer load charging curve - special relay sauce
                    // Original formula (buffer - 3*maxPower)^2 / (3 * maxPower) - (maxPower / 3)
                    //loadDraw = MathHelper.Clamp((float)Math.Pow(internalBuffer - 3*MaxPower,2) / (3*MaxPower) - MaxPower/3, 1, MaxPower);
                    //Optimised formula 0.2% error from original
                    loadDraw = MathHelper.Clamp(internalBuffer * internalBuffer * thirdInverseMax - 2 * internalBuffer + loadEqnConstant, 0.001f, MaxPower);

                    //Slight smoothing to load to minimise relay jank
                    loadDraw = MathHelper.Clamp((loadDraw + prevLoad * 0.1f) / 1.1f, 0.001f, MaxPower);
                    prevLoad = loadDraw;
                }

                //Add on extra load after calculation
                loadDraw += ExtraLoad;
                return loadDraw;
            }
            else
            {
                //Flag output as power out
                return -1;
            }
        }

        private bool RelayCanOutput()
        {
            //Only allow output if device is on, buffers have charge and the connected grids aren't short circuited
            return isOn && powerIn != null && powerIn.Grid != null && internalBuffer > 0 && powerOut != null && powerOut.Grid != null && powerIn.Grid != powerOut.Grid;
        }

        /// <summary>
        /// Minimum and maximum power out for the relay.
        /// Max out is adjusted to allow for other relays to compensate
        /// if this relay is undervolted.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="load"></param>
        /// <returns></returns>
        public override Vector3 MinMaxPowerOut(Connection conn, float load = 0)
        {
            Vector3 minMaxPower = new Vector3();
            if (conn == powerIn)
            {
                minMaxPower.X = CurrPowerConsumption;
                minMaxPower.Y = CurrPowerConsumption;
            }
            else
            {
                if (RelayCanOutput())
                {
                    //Determine output limits from buffer and voltage
                    float bufferDraw = MathHelper.Min(internalBuffer, MaxPower);
                    float voltageLimit = MathHelper.Min(MaxPower, load) * prevVoltage;

                    //If undervolted adjust max output so that other relays can compensate
                    if (prevVoltage < 1)
                    {
                        voltageLimit *= prevVoltage;
                    }

                    minMaxPower.Y = -MathHelper.Min(voltageLimit, bufferDraw);
                }
            }

            return minMaxPower;
        }

        /// <summary>
        /// Power out for the relay connection
        /// Relay will output the necessary power to the grid based on maximum power output of other
        /// relays and will undervolt and overvolt the grid following its supply grid
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="power"></param>
        /// <param name="minMaxPower"></param>
        /// <param name="load"></param>
        /// <returns>Power outputted to the grid (negative is adding power)</returns>
        public override float ConnPowerOut(Connection conn, float power, Vector3 minMaxPower, float load)
        {
            if (conn == powerIn)
            {
                return CurrPowerConsumption;
            }
            else if (RelayCanOutput())
            {
                //Determine output limits of the relay
                float bufferDraw = MathHelper.Min(internalBuffer, MaxPower);
                float voltageLimit = MathHelper.Min(MaxPower, load) * prevVoltage;
                float maxOut = -MathHelper.Min(voltageLimit, bufferDraw);

                //Don't output negative power to the grid
                if (maxOut >= 0)
                {
                    PowerLoad = 0;
                    return 0;
                }

                prevOutputLoad = load;

                //Calculate power out
                PowerLoad = MathHelper.Clamp((load * prevVoltage - power) / MathHelper.Max(minMaxPower.Y, 1E-20f) * maxOut, maxOut, 0);
                return PowerLoad;
            }
            
            //Else relay isn't outputting
            PowerLoad = 0;
            return 0;
        }

        /// <summary>
        /// Connection's grid resolved, determine the difference to be added to the buffer
        /// Ensure the prevVoltage voltage is updated once both grids are resolved
        /// </summary>
        /// <param name="conn"></param>
        public override void GridResolved(Connection conn) {
            if (conn == powerIn)
            {
                if (powerIn != null && powerIn.Grid != null)
                {
                    float addToBuffer = powerIn.Grid.Voltage * (CurrPowerConsumption - ExtraLoad);

                    //Limit power input to the previous voltage to prevent wild oscilations in overload
                    if (powerIn.Grid.Voltage > 1)
                    {
                        addToBuffer = prevVoltage * (CurrPowerConsumption - ExtraLoad);
                    }

                    //Cap the max power input
                    if (addToBuffer > MaxPower)
                    {
                        addToBuffer = MaxPower;
                    }

                    //To prevent problems with grid order, only update voltage and buffer after input and output grids have been resolved
                    if (newVoltage == null)
                    {
                        //temporarily store the new voltage and also indicates that the input connection side has been updated
                        newVoltage = powerIn.Grid.Voltage;
                        bufferdiff = addToBuffer;
                    }
                    else
                    {
                        updateBuffer(addToBuffer, powerIn.Grid.Voltage);
                    }
                }
            }
            else
            {
                //To prevent problems with grid order, only update voltage and buffer after input and output grids have been resolved
                if (newVoltage == null)
                {
                    //Flag that output conenction has been updated already
                    newVoltage = -1;
                    bufferdiff = PowerLoad;
                }
                else
                {
                    updateBuffer(PowerLoad, (float)newVoltage);
                }
            }
        }

        private void updateBuffer(float addToBuffer, float newvoltage)
        {
            //Update buffer and voltage
            float limit = MaxPower * 2;

            //Clamp the buffer to have a constant load in a severe overload event, otherwise wild oscillation will occur
            if (RelayCanOutput() && powerIn.Grid.Voltage > 2)
            {
                limit = MathHelper.Min(limit, 3 * MaxPower - (float)Math.Sqrt((3 * prevOutputLoad + MaxPower) * MaxPower));
            }

            //Add to the internal buffer
            internalBuffer = MathHelper.Clamp(internalBuffer + bufferdiff + addToBuffer, 0, limit);

            //Decay overvoltage slightly, helps resolve large chain loops to a grid
            if (newvoltage > 1)
            {
                newvoltage = MathHelper.Max(newvoltage - 0.0005f, 1);
            }

            prevVoltage = newvoltage;
            newVoltage = null;
            bufferdiff = 0;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (item.Condition <= 0.0f || connection.IsPower) { return; }

            if (connectionPairs.TryGetValue(connection.Name, out string outConnection))
            {
                if (!IsOn) { return; }
                item.SendSignal(signal, outConnection);
            }
            else if (connection.Name == "toggle")
            {
                if (signal.value == "0") { return; }
                SetState(!IsOn, false);
            }
            else if (connection.Name == "set_state")
            {
                SetState(signal.value != "0", false);
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
