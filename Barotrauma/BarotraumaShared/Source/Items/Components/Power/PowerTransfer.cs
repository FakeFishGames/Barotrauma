using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerTransfer : Powered
    {
        private static float fullPower;
        private static float fullLoad;

        private int updateCount;

        //affects how fast changes in power/load are carried over the grid
        static float inertia = 5.0f;

        private static HashSet<Powered> connectedList = new HashSet<Powered>();
        private List<Connection> powerConnections;
        public List<Connection> PowerConnections
        {
            get
            {
                return powerConnections;
            }
        }


        private Dictionary<Connection, bool> connectionDirty = new Dictionary<Connection, bool>();

        //a list of connections a given connection is connected to, either directly or via other power transfer components
        private Dictionary<Connection, HashSet<Connection>> connectedRecipients = new Dictionary<Connection, HashSet<Connection>>();

        private float powerLoad;

        private bool isBroken;

        public float PowerLoad
        {
            get { return powerLoad; }
        }

        [Serialize(true, true), Editable(ToolTip = "Can the item be damaged if too much power is supplied to the power grid.")]
        public bool CanBeOverloaded
        {
            get;
            set;
        }

        [Serialize(2.0f, true), Editable(MinValueFloat = 1.0f, ToolTip = 
            "How much power has to be supplied to the grid relative to the load before item starts taking damage. "
            +"E.g. a value of 2 means that the grid has to be receiving twice as much power as the devices in the grid are consuming.")]
        public float OverloadVoltage
        {
            get;
            set;
        }

        [Serialize(0.15f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, ToolTip = "The probability for a fire to start when the item breaks.")]
        public float FireProbability
        {
            get;
            set;
        }

        //can the component transfer power
        private bool canTransfer;
        public bool CanTransfer
        {
            get { return canTransfer; }
            set
            {
                if (canTransfer == value) return;
                canTransfer = value;
                SetAllConnectionsDirty();
            }
        }

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }

            set
            {
                if (base.IsActive == value) return;
                base.IsActive = value;
                powerLoad = 0.0f;
                currPowerConsumption = 0.0f;

                SetAllConnectionsDirty();
                if (!base.IsActive)
                {
                    //we need to refresh the connections here because Update won't be called on inactive components
                    RefreshConnections();
                }
            }
        }

        public PowerTransfer(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            canTransfer = true;

            InitProjectSpecific(element);
        }
        
        partial void InitProjectSpecific(XElement element);

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);

            if (!isBroken)
            {
                powerLoad = 0.0f;
                currPowerConsumption = 0.0f;
                SetAllConnectionsDirty();
                RefreshConnections();
                isBroken = true;
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            RefreshConnections();
            if (!CanTransfer) return;

            if (isBroken)
            {
                SetAllConnectionsDirty();
                isBroken = false;
            }
            
            if (updateCount > 0)
            {
                //this junction box has already been updated this frame
                updateCount--;
                return;
            }

            //reset and recalculate the power generated/consumed
            //by the constructions connected to the grid
            fullPower = 0.0f;
            fullLoad = 0.0f;

            connectedList.Clear();

            updateCount = 0;
            CheckJunctions(deltaTime);

            foreach (Powered p in connectedList)
            {
                PowerTransfer pt = p as PowerTransfer;
                if (pt == null || pt.updateCount == 0) continue;

                if (pt is RelayComponent != this is RelayComponent) continue;

                pt.powerLoad += (fullLoad - pt.powerLoad) / inertia;
                pt.currPowerConsumption += (-fullPower - pt.currPowerConsumption) / inertia;

                float voltage = fullPower / Math.Max(fullLoad, 1.0f);
                if (this is RelayComponent)
                {
                    pt.currPowerConsumption = Math.Max(-fullLoad, pt.currPowerConsumption);
                    voltage = Math.Min(voltage, 1.0f);
                }

                pt.Item.SendSignal(0, "", "power", null, voltage);
                pt.Item.SendSignal(0, "", "power_out", null, voltage);

#if CLIENT
                //damage the item if voltage is too high 
                //(except if running as a client)
                if (GameMain.Client != null) continue;
#endif

                //items in a bad condition are more sensitive to overvoltage
                float maxOverVoltage = MathHelper.Lerp(OverloadVoltage * 0.75f, OverloadVoltage, item.Condition / item.MaxCondition);
                maxOverVoltage = Math.Max(OverloadVoltage, 1.0f);

                //if the item can't be fixed, don't allow it to break
                if (!item.Repairables.Any() || !CanBeOverloaded) continue;

                //relays don't blow up if the power is higher than load, only if the output is high enough 
                //(i.e. enough power passing through the relay)
                if (this is RelayComponent) continue;

                if (-pt.currPowerConsumption < Math.Max(pt.powerLoad, 200.0f) * maxOverVoltage) continue;

                float prevCondition = pt.item.Condition;
                pt.item.Condition -= deltaTime * 10.0f;

                if (pt.item.Condition <= 0.0f && prevCondition > 0.0f)
                {
#if CLIENT
                    SoundPlayer.PlaySound("zap", item.WorldPosition, hullGuess: item.CurrentHull);                    

                    Vector2 baseVel = Rand.Vector(300.0f);
                    for (int i = 0; i < 10; i++)
                    {
                        var particle = GameMain.ParticleManager.CreateParticle("spark", pt.item.WorldPosition,
                            baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);

                        if (particle != null) particle.Size *= Rand.Range(0.5f, 1.0f);
                    }
#endif

                    float currentIntensity = GameMain.GameSession?.EventManager != null ? 
                        GameMain.GameSession.EventManager.CurrentIntensity : 0.5f;
                    
                    //higher probability for fires if the current intensity is low
                    if (FireProbability > 0.0f && 
                        Rand.Range(0.0f, 1.0f) < MathHelper.Lerp(FireProbability, FireProbability * 0.1f, currentIntensity))
                    {
                        new FireSource(pt.item.WorldPosition);
                    }
                }
            }

            updateCount = 0;
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        private void RefreshConnections()
        {
            var connections = item.Connections;
            foreach (Connection c in connections)
            {
                if (!connectionDirty.ContainsKey(c))
                {
                    connectionDirty[c] = true;
                }
                else if (!connectionDirty[c])
                {
                    continue;
                }

                HashSet<Connection> connected = new HashSet<Connection>();
                if (!connectedRecipients.ContainsKey(c))
                {
                    connectedRecipients.Add(c, connected);
                }
                else
                {
                    //mark all previous recipients as dirty
                    foreach (Connection recipient in connectedRecipients[c])
                    {
                        var pt = recipient.Item.GetComponent<PowerTransfer>();
                        if (pt != null) pt.connectionDirty[recipient] = true;
                    }
                }

                //find all connections that are connected to this one (directly or via another PowerTransfer)
                connected.Add(c);
                GetConnected(c, connected);
                connectedRecipients[c] = connected;

                //go through all the PowerTransfers and we're connected to and set their connections to match the ones we just calculated
                //(no need to go through the recursive GetConnected method again)
                foreach (Connection recipient in connected)
                {
                    var recipientPowerTransfer = recipient.Item.GetComponent<PowerTransfer>();
                    if (recipientPowerTransfer == null) continue;

                    if (!connectedRecipients.ContainsKey(recipient))
                    {
                        connectedRecipients.Add(recipient, connected);
                    }

                    recipientPowerTransfer.connectedRecipients[recipient] = connected;
                    recipientPowerTransfer.connectionDirty[recipient] = false;
                }
            }
        }

        //Finds all the connections that can receive a signal sent into the given connection and stores them in the hashset.
        private void GetConnected(Connection c, HashSet<Connection> connected)
        {
            var recipients = c.Recipients;

            foreach (Connection recipient in recipients)
            {
                if (recipient == null || connected.Contains(recipient)) continue;

                Item it = recipient.Item;
                if (it == null || it.Condition <= 0.0f) continue;

                connected.Add(recipient);

                var powerTransfer = it.GetComponent<PowerTransfer>();
                if (powerTransfer != null && powerTransfer.CanTransfer && powerTransfer.IsActive)
                {
                    GetConnected(recipient, connected);
                }
            }
        }

        //a recursive function that goes through all the junctions and adds up
        //all the generated/consumed power of the constructions connected to the grid
        private void CheckJunctions(float deltaTime, bool increaseUpdateCount = true, float clampPower = float.MaxValue, float clampLoad = float.MaxValue)
        {
            if (increaseUpdateCount)
            {
                updateCount = 1;
            }
            connectedList.Add(this);

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            //float maxPower = this is RelayComponent relayComponent ? relayComponent.MaxPower : float.PositiveInfinity;
            RelayComponent thisRelayComponent = this as RelayComponent;
            if (thisRelayComponent != null)
            {
                clampPower = Math.Min(Math.Min(clampPower, thisRelayComponent.MaxPower), powerLoad);
                clampLoad = Math.Min(clampLoad, thisRelayComponent.MaxPower);
            }

            foreach (Connection c in PowerConnections)
            {
                var recipients = c.Recipients;
                foreach (Connection recipient in recipients)
                {
                    if (recipient?.Item == null) continue;

                    Item it = recipient.Item;
                    if (it.Condition <= 0.0f) continue;

                    foreach (ItemComponent ic in it.Components)
                    {
                        if (!(ic is Powered powered) || !powered.IsActive) continue;
                        if (connectedList.Contains(powered)) continue;

                        if (powered is PowerTransfer powerTransfer)
                        {
                            RelayComponent otherRelayComponent = powerTransfer as RelayComponent;
                            if ((thisRelayComponent == null) == (otherRelayComponent == null))
                            {
                                if (!powerTransfer.CanTransfer) continue;
                                powerTransfer.CheckJunctions(deltaTime, increaseUpdateCount, clampPower, clampLoad);
                            }
                            else
                            {
                                if (!powerTransfer.CanTransfer) continue;
                                float maxPowerIn = (thisRelayComponent != null && c.IsOutput) ? 0.0f : clampPower;
                                float maxPowerOut = (thisRelayComponent != null && !c.IsOutput) ? 0.0f : clampLoad;
                                if (maxPowerIn > 0.0f || maxPowerOut > 0.0f)
                                {
                                    powerTransfer.CheckJunctions(deltaTime, false,  maxPowerIn, maxPowerOut);
                                }
                            }

                            continue;
                        }

                        float addLoad = 0.0f;
                        float addPower = 0.0f;
                        if (powered is PowerContainer powerContainer)
                        {
                            if (recipient.Name == "power_in")
                            {
                                addLoad = powerContainer.CurrPowerConsumption;
                            }
                            else
                            {
                                addPower = powerContainer.CurrPowerOutput;
                            }
                        }
                        else
                        {
                            connectedList.Add(powered);
                            //positive power consumption = the construction requires power -> increase load
                            if (powered.CurrPowerConsumption > 0.0f)
                            {
                                addLoad = powered.CurrPowerConsumption;
                            }
                            else if (powered.CurrPowerConsumption < 0.0f)
                            //negative power consumption = the construction is a 
                            //generator/battery or another junction box
                            {
                                addPower -= powered.CurrPowerConsumption;
                            }
                        }

                        if (addPower + fullPower > clampPower) { addPower -= (addPower + fullPower) - clampPower; };
                        if (addPower > 0) { fullPower += addPower; }

                        if (addLoad + fullLoad > clampLoad) { addLoad -= (addLoad + fullLoad) - clampLoad; };
                        if (addLoad > 0) { fullLoad += addLoad; }
                    }
                }
            }
        }

        public void SetAllConnectionsDirty()
        {
            if (item.Connections == null) return;
            foreach (Connection c in item.Connections)
            {
                connectionDirty[c] = true;
            }
        }

        public void SetConnectionDirty(Connection connection)
        {
            var connections = item.Connections;
            if (connections == null || !connections.Contains(connection)) return;
            connectionDirty[connection] = true;
        }

        public override void OnItemLoaded()
        {
            var connections = Item.Connections;
            powerConnections = connections == null ? new List<Connection>() : connections.FindAll(c => c.IsPower);  
            if (connections == null)
            {
                IsActive = false;
                return;
            }
            SetAllConnectionsDirty();
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power, float signalStrength = 1.0f)
        {
            if (connection.IsPower) return;

            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);

            if (!connectedRecipients.ContainsKey(connection)) return;

            if (connection.Name.Length > 5 && connection.Name.Substring(0, 6) == "signal")
            {
                foreach (Connection recipient in connectedRecipients[connection])
                {
                    if (recipient.Item == item || recipient.Item == source) continue;

                    foreach (ItemComponent ic in recipient.Item.Components)
                    {
                        //powertransfer components don't need to receive the signal in the pass-through signal connections
                        //because we relay it straight to the connected items without going through the whole chain of junction boxes
                        if (ic is PowerTransfer && connection.Name.Contains("signal")) continue;
                        ic.ReceiveSignal(stepsTaken, signal, recipient, source, sender, 0.0f, signalStrength);
                    }

                    bool broken = recipient.Item.Condition <= 0.0f;
                    foreach (StatusEffect effect in recipient.effects)
                    {
                        if (broken && effect.type != ActionType.OnBroken) continue;
                        recipient.Item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f, null, null, false, false);
                    }
                }
            }
        }
    }
}
