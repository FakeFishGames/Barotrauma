using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Reactor : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        const float NetworkUpdateInterval = 0.5f;

        //the rate at which the reactor is being run un
        //higher rates generate more power (and heat)
        private float fissionRate;

        //the rate at which the heat is being dissipated
        private float coolingRate;

        private float temperature;

        private Client BlameOnBroken;

        //is automatic temperature control on
        //(adjusts the cooling rate automatically to keep the
        //amount of power generated balanced with the load)
        private bool autoTemp;

        //the temperature after which fissionrate is automatically 
        //turned down and cooling increased
        private float shutDownTemp;

        private float fireTemp, meltDownTemp, meltDownDelay;

        private float meltDownTimer;

        //how much power is provided to the grid per 1 temperature unit
        private float powerPerTemp;

        private float load;
        
        private bool unsentChanges;
        private float sendUpdateTimer;

        private Character lastUser;
        private float? nextServerLogWriteTime;
        private float lastServerLogWriteTime;

        [Editable(ToolTip = "The temperature at which the reactor melts down."), Serialize(9500.0f, true)]
        public float MeltDownTemp
        {
            get { return meltDownTemp; }
            set 
            {
                meltDownTemp = Math.Max(0.0f, value);
            }
        }

        [Serialize(30.0f, true)]
        public float MeltdownDelay
        {
            get { return meltDownDelay; }
            set { meltDownDelay = Math.Max(value, 0.0f); }
        }

        [Editable(ToolTip = "The temperature at which the reactor catches fire."), Serialize(9000.0f, true)]
        public float FireTemp
        {
            get { return fireTemp; }
            set
            {
                fireTemp = Math.Max(0.0f, value);
            }
        }

        [Editable(0.0f, float.MaxValue, ToolTip = "How much power (kW) the reactor generates relative to it's operating temperature (kW per one degree Celsius)."), Serialize(1.0f, true)]
        public float PowerPerTemp
        {
            get { return powerPerTemp; }
            set
            {
                powerPerTemp = Math.Max(0.0f, value);
            }
        }

        [Serialize(0.0f, true)]
        public float FissionRate
        {
            get { return fissionRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                fissionRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }

        [Serialize(0.0f, true)]
        public float CoolingRate
        {
            get { return coolingRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                coolingRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }

        [Serialize(0.0f, true)]
        public float Temperature
        {
            get { return temperature; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                temperature = MathHelper.Clamp(value, 0.0f, 10000.0f); 
            }
        }

        public bool IsRunning()
        {
            return (temperature > 0.0f);
        }

        [Serialize(false, true)]
        public bool AutoTemp
        {
            get { return autoTemp; }
            set 
            { 
                autoTemp = value;
#if CLIENT
                if (autoTempTickBox!=null) autoTempTickBox.Selected = value;
#endif
            }
        }

        public float ExtraCooling { get; set; }

        public float AvailableFuel { get; set; }

        private float availableHeat, availableCooling;
        private float prevTemperature, temperatureChange;

        [Serialize(500.0f, true)]
        public float ShutDownTemp
        {
            get { return shutDownTemp; }
            set { shutDownTemp = MathHelper.Clamp(value, 0.0f, 10000.0f); }
        }

        public Reactor(Item item, XElement element)
            : base(item, element)
        {
            shutDownTemp = 500.0f;
            powerPerTemp = 1.0f;            
            IsActive = true;
            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override void Update(float deltaTime, Camera cam) 
        {
            if (GameMain.Server != null && nextServerLogWriteTime != null)
            {
                if (Timing.TotalTime >= (float)nextServerLogWriteTime)
                {
                    GameServer.Log(lastUser.LogName + " adjusted reactor settings: " +
                            "Temperature: " + (int)temperature +
                            ", Fission rate: " + (int)fissionRate +
                            ", Cooling rate: " + (int)coolingRate +
                            ", Cooling rate: " + coolingRate +
                            ", Shutdown temp: " + shutDownTemp +
                            (autoTemp ? ", Autotemp ON" : ", Autotemp OFF"),
                            ServerLog.MessageType.Reactor);
                    
                    nextServerLogWriteTime = null;
                    lastServerLogWriteTime = (float)Timing.TotalTime;
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            fissionRate = Math.Min(fissionRate, AvailableFuel);

            //the amount of cooling is always non-zero, so that the reactor always needs 
            //to generate some amount of heat to prevent the temperature from dropping
            availableCooling = Math.Max(ExtraCooling, 5.0f);
            availableHeat = 80 * (AvailableFuel / 2000.0f);

            float heat = availableHeat * fissionRate;
            float heatDissipation = 50 * coolingRate + availableCooling;

            float deltaTemp = (((heat - heatDissipation) * 5) - temperature) / 10000.0f;            
            Temperature = temperature + deltaTemp;

            temperatureChange = Temperature - prevTemperature;
            prevTemperature = temperature;

            if (temperature > fireTemp && temperature - deltaTemp < fireTemp)
            {
#if CLIENT
                Vector2 baseVel = Rand.Vector(300.0f);
                for (int i = 0; i < 10; i++)
                {
                    var particle = GameMain.ParticleManager.CreateParticle("spark", item.WorldPosition,
                        baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);

                    if (particle != null) particle.Size *= Rand.Range(0.5f, 1.0f);
                }
#endif

                new FireSource(item.WorldPosition);
            }

            if (temperature > meltDownTemp)
            {
                item.SendSignal(0, "1", "meltdown_warning", null);
                meltDownTimer += deltaTime;

                if (meltDownTimer > MeltdownDelay)
                {
                    MeltDown();
                    return;
                }
            }
            else
            {
                item.SendSignal(0, "0", "meltdown_warning", null);
                meltDownTimer = Math.Max(0.0f, meltDownTimer - deltaTime);
            }

            load = 0.0f;

            List<Connection> connections = item.Connections;
            if (connections != null && connections.Count > 0)
            {
                foreach (Connection connection in connections)
                {
                    if (!connection.IsPower) continue;
                    foreach (Connection recipient in connection.Recipients)
                    {
                        Item it = recipient.Item as Item;
                        if (it == null) continue;

                        PowerTransfer pt = it.GetComponent<PowerTransfer>();
                        if (pt == null) continue;
                        
                        load = Math.Max(load,pt.PowerLoad); 
                    }
                }
            }
            
            //item.Condition -= temperature * deltaTime * 0.00005f;

            if (temperature > shutDownTemp)
            {
                CoolingRate += 0.5f;
                FissionRate -= 0.5f;
            }
            else if (autoTemp)
            {
                //take deltaTemp into account to slow down the change in temperature when getting closer to the desired value
                float target = temperature + deltaTemp * 100.0f;

                //-1.0f in order to gradually turn down both rates when the target temperature is reached
                FissionRate += (MathHelper.Clamp(load - target, -10.0f, 10.0f) - 1.0f) * deltaTime;
                CoolingRate += (MathHelper.Clamp(target - load, -5.0f, 5.0f) - 1.0f) * deltaTime;
            }
            
            //the power generated by the reactor is equal to the temperature
            currPowerConsumption = -temperature*powerPerTemp;
            
            if (item.CurrentHull != null)
            {
                //the sound can be heard from 20 000 display units away when running at full power
                item.CurrentHull.SoundRange = Math.Max(temperature * 2, item.CurrentHull.AiTarget.SoundRange);
            }

#if CLIENT
            UpdateGraph(deltaTime);
#endif

            ExtraCooling = 0.0f;
            AvailableFuel = 0.0f;

            item.SendSignal(0, ((int)temperature).ToString(), "temperature_out", null);
              
            sendUpdateTimer = Math.Max(sendUpdateTimer - deltaTime, 0.0f);

            if (unsentChanges && sendUpdateTimer<= 0.0f)
            {
                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                }
#if CLIENT
                else if (GameMain.Client != null)
                {
                    item.CreateClientEvent(this);
                }
#endif

                sendUpdateTimer = NetworkUpdateInterval;
                unsentChanges = false;
            }            
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);

            Temperature -= deltaTime * 1000.0f;
            FissionRate -= deltaTime * 10.0f;
            CoolingRate -= deltaTime * 10.0f;

            currPowerConsumption = -temperature;

#if CLIENT
            UpdateGraph(deltaTime);
#endif

            ExtraCooling = 0.0f;
        }

        private void MeltDown()
        {
            if (item.Condition <= 0.0f || GameMain.Client != null) return;

            GameServer.Log("Reactor meltdown!", ServerLog.MessageType.Reactor);
 
            item.Condition = 0.0f;

            var containedItems = item.ContainedItems;
            if (containedItems != null)
            {
                foreach (Item containedItem in containedItems)
                {
                    if (containedItem == null) continue;
                    containedItem.Condition = 0.0f;
                }
            }
            
            if (GameMain.Server != null && GameMain.Server.ConnectedClients.Contains(BlameOnBroken))
            {
                BlameOnBroken.Karma = 0.0f;
            }            
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            float degreeOfSuccess = DegreeOfSuccess(character);

            //characters with insufficient skill levels don't refuel the reactor
            if (degreeOfSuccess > 0.2f)
            {
                //remove used-up fuel from the reactor
                var containedItems = item.ContainedItems;
                foreach (Item item in containedItems)
                {
                    if (item != null && item.Condition <= 0.0f)
                    {
                        item.Drop();
                    }
                }

                //the temperature is too low and not increasing even though the fission rate is high and cooling low
                // -> we need more fuel
                if (temperature < load * 0.5f && temperatureChange <= 0.0f && fissionRate > 0.9f && coolingRate < 0.1f)
                {
                    var containFuelObjective = new AIObjectiveContainItem(character, new string[] { "Fuel Rod", "reactorfuel" }, item.GetComponent<ItemContainer>());
                    containFuelObjective.MinContainedAmount = containedItems.Count(i => i != null && i.Prefab.NameMatches("Fuel Rod") || i.HasTag("reactorfuel")) + 1;
                    containFuelObjective.GetItemPriority = (Item fuelItem) =>
                    {
                        if (fuelItem.ParentInventory?.Owner is Item)
                        {
                            //don't take fuel from other reactors
                            if (((Item)fuelItem.ParentInventory.Owner).GetComponent<Reactor>() != null) return 0.0f;
                        }
                        return 1.0f;
                    };
                    objective.AddSubObjective(containFuelObjective);

                    return false;
                }
            }


            switch (objective.Option.ToLowerInvariant())
            {
                case "power up":
                    float tempDiff = load - temperature;

                    shutDownTemp = Math.Min(load + 1000.0f, 7500.0f);

                    //characters with insufficient skill levels simply set the autotemp on instead of trying to adjust the temperature manually
                    if (Math.Abs(tempDiff) < 500.0f || degreeOfSuccess < 0.5f)
                    {
                        AutoTemp = true;
                    }
                    else
                    {
                        AutoTemp = false;
                        //higher skill levels make the character adjust the temperature faster
                        FissionRate += deltaTime * 100.0f * Math.Sign(tempDiff) * degreeOfSuccess;
                        CoolingRate -= deltaTime * 100.0f * Math.Sign(tempDiff) * degreeOfSuccess;
                    }                    
                    break;
                case "shutdown":
                    shutDownTemp = 0.0f;
                    break;
            }

            return false;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power)
        {
            switch (connection.Name)
            {
                case "shutdown":
                    if (shutDownTemp > 0.0f)
                    {
                        unsentChanges = true;
                        shutDownTemp = 0.0f;
                    }
                    break;
            }
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            bool autoTemp       = msg.ReadBoolean();
            float shutDownTemp  = msg.ReadRangedSingle(0.0f, 10000.0f, 15);
            float coolingRate   = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            float fissionRate   = msg.ReadRangedSingle(0.0f, 100.0f, 8);

            if (!item.CanClientAccess(c)) return;

            if (!autoTemp && AutoTemp) BlameOnBroken = c;
            if (shutDownTemp > ShutDownTemp) BlameOnBroken = c;
            if (fissionRate > FissionRate) BlameOnBroken = c;
            
            AutoTemp = autoTemp;
            ShutDownTemp = shutDownTemp;

            CoolingRate = coolingRate;
            FissionRate = fissionRate;

            lastUser = c.Character;
            if (nextServerLogWriteTime == null)
            {
                nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
            }

            //need to create a server event to notify all clients of the changed state
            unsentChanges = true;
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedSingle(temperature, 0.0f, 10000.0f, 16);

            msg.Write(autoTemp);
            msg.WriteRangedSingle(shutDownTemp, 0.0f, 10000.0f, 15);

            msg.WriteRangedSingle(coolingRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(fissionRate, 0.0f, 100.0f, 8);
        }
    }
}
