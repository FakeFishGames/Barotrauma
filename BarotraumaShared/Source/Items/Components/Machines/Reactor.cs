using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;

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

        //is automatic temperature control on
        //(adjusts the cooling rate automatically to keep the
        //amount of power generated balanced with the load)
        private bool autoTemp;

        //the temperature after which fissionrate is automatically 
        //turned down and cooling increased
        private float shutDownTemp;

        private float fireTemp, meltDownTemp;

        //how much power is provided to the grid per 1 temperature unit
        private float powerPerTemp;

        private int graphSize = 25;

        private float graphTimer;

        private int updateGraphInterval = 500;

        private float[] fissionRateGraph;
        private float[] coolingRateGraph;
        private float[] tempGraph;
        private float[] loadGraph;

        private float load;
        
        private PropertyTask powerUpTask;

        private bool unsentChanges;
        private float sendUpdateTimer;

        private Character lastUser;
        private float? nextServerLogWriteTime;
        private float lastServerLogWriteTime;

        [Editable, HasDefaultValue(9500.0f, true)]
        public float MeltDownTemp
        {
            get { return meltDownTemp; }
            set 
            {
                meltDownTemp = Math.Max(0.0f, value);
            }
        }

        [Editable, HasDefaultValue(9000.0f, true)]
        public float FireTemp
        {
            get { return fireTemp; }
            set
            {
                fireTemp = Math.Max(0.0f, value);
            }
        }

        [Editable, HasDefaultValue(1.0f, true)]
        public float PowerPerTemp
        {
            get { return powerPerTemp; }
            set
            {
                powerPerTemp = Math.Max(0.0f, value);
            }
        }

        [HasDefaultValue(0.0f, true)]
        public float FissionRate
        {
            get { return fissionRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                fissionRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }

        [HasDefaultValue(0.0f, true)]
        public float CoolingRate
        {
            get { return coolingRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                coolingRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }

        [HasDefaultValue(0.0f, true)]
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

        [HasDefaultValue(false, true)]
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

        [HasDefaultValue(500.0f, true)]
        public float ShutDownTemp
        {
            get { return shutDownTemp; }
            set { shutDownTemp = MathHelper.Clamp(value, 0.0f, 10000.0f); }
        }

        public Reactor(Item item, XElement element)
            : base(item, element)
        {
            fissionRateGraph    = new float[graphSize];
            coolingRateGraph    = new float[graphSize];
            tempGraph           = new float[graphSize];
            loadGraph           = new float[graphSize];

            shutDownTemp = 500.0f;

            powerPerTemp = 1.0f;
            
            IsActive = true;

            InitProjSpecific();
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            if (GameMain.Server != null && nextServerLogWriteTime != null)
            {
                if (Timing.TotalTime >= (float)nextServerLogWriteTime)
                {
                    GameServer.Log(lastUser + " adjusted reactor settings: " +
                            "Temperature: " + (int)temperature +
                            ", Fission rate: " + (int)fissionRate +
                            ", Cooling rate: " + (int)coolingRate +
                            ", Cooling rate: " + coolingRate +
                            ", Shutdown temp: " + shutDownTemp +
                            (autoTemp ? ", Autotemp ON" : ", Autotemp OFF"),
                            ServerLog.MessageType.ItemInteraction);
                    
                    nextServerLogWriteTime = null;
                    lastServerLogWriteTime = (float)Timing.TotalTime;
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            
            fissionRate = Math.Min(fissionRate, AvailableFuel);
            
            float heat = 80 * fissionRate * (AvailableFuel/2000.0f);
            float heatDissipation = 50 * coolingRate + Math.Max(ExtraCooling, 5.0f);

            float deltaTemp = (((heat - heatDissipation) * 5) - temperature) / 10000.0f;
            Temperature = temperature + deltaTemp;

            if (temperature>fireTemp && temperature-deltaTemp<fireTemp)
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
                MeltDown();
                return;
            }
            else if (temperature == 0.0f)
            {
                if (powerUpTask == null || powerUpTask.IsFinished)
                {
                    powerUpTask = new PropertyTask(item, IsRunning, 50.0f, "Power up the reactor");
                }
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
            if (item.Condition <= 0.0f) return;

            GameServer.Log("Reactor meltdown!", ServerLog.MessageType.ItemInteraction);
 
            new RepairTask(item, 60.0f, "Reactor meltdown!");
            item.Condition = 0.0f;

            var containedItems = item.ContainedItems;
            if (containedItems == null) return;
            
            foreach (Item containedItem in item.ContainedItems)
            {
                if (containedItem == null) continue;
                containedItem.Condition = 0.0f;
            }
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            switch (objective.Option.ToLowerInvariant())
             {
                 case "power up":
                     float tempDiff = load - temperature;

                     shutDownTemp = Math.Min(load + 1000.0f, 7500.0f);

                     //temperature too high/low
                     if (Math.Abs(tempDiff)>500.0f)
                     {
                         AutoTemp = false;
                         FissionRate += deltaTime * 100.0f * Math.Sign(tempDiff);
                         CoolingRate -= deltaTime * 100.0f * Math.Sign(tempDiff);
                     }
                     //temperature OK
                     else
                     {
                         AutoTemp = true;
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
