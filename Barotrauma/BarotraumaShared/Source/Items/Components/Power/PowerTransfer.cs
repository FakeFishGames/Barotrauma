using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerTransfer : Powered
    {
        static float fullPower;
        static float fullLoad;

        //private bool updated;

        private int updateTimer;
        
        const float FireProbability = 0.15f;

        //affects how fast changes in power/load are carried over the grid
        static float inertia = 5.0f;

        static HashSet<Powered> connectedList = new HashSet<Powered>();

        private List<Connection> powerConnections;

        private float powerLoad;

        public float PowerLoad
        {
            get { return powerLoad; }
        }

        public PowerTransfer(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            powerConnections = new List<Connection>();
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            if (updateTimer > 0)
            {
                //this junction box has already been updated this frame
                updateTimer--;
                return;
            }

            //reset and recalculate the power generated/consumed
            //by the constructions connected to the grid
            fullPower = 0.0f;
            fullLoad = 0.0f;    
               
            connectedList.Clear();

            CheckJunctions(deltaTime);
            updateTimer = 0;

            foreach (Powered p in connectedList)
            {
                PowerTransfer pt = p as PowerTransfer;
                if (pt == null) continue;
                
                pt.powerLoad += (fullLoad - pt.powerLoad) / inertia;
                pt.currPowerConsumption += (-fullPower - pt.currPowerConsumption) / inertia;
                pt.Item.SendSignal(0, "", "power", null, fullPower / Math.Max(fullLoad, 1.0f));
                pt.Item.SendSignal(0, "", "power_out", null, fullPower / Math.Max(fullLoad, 1.0f));

                //damage the item if voltage is too high 
                //(except if running as a client)
                if (GameMain.Client != null) continue;
                if (-pt.currPowerConsumption < Math.Max(pt.powerLoad * Rand.Range(1.9f,2.1f), 200.0f)) continue;
                                
                float prevCondition = pt.item.Condition;
                pt.item.Condition -= deltaTime * 10.0f;

                if (pt.item.Condition <= 0.0f && prevCondition > 0.0f)
                {
#if CLIENT
                    sparkSounds[Rand.Int(sparkSounds.Length)].Play(1.0f, 600.0f, pt.item.WorldPosition);

                    Vector2 baseVel = Rand.Vector(300.0f);
                    for (int i = 0; i < 10; i++)
                    {
                        var particle = GameMain.ParticleManager.CreateParticle("spark", pt.item.WorldPosition,
                            baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);

                        if (particle != null) particle.Size *= Rand.Range(0.5f, 1.0f);
                    }
#endif

                    if (FireProbability > 0.0f && Rand.Int((int)(1.0f / FireProbability)) == 1)
                    {
                        new FireSource(pt.item.WorldPosition);
                    }
                }
                  
            }
        }

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        //a recursive function that goes through all the junctions and adds up
        //all the generated/consumed power of the constructions connected to the grid
        private void CheckJunctions(float deltaTime)
        {
            updateTimer = 1;
            connectedList.Add(this);

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            
            foreach (Connection c in powerConnections)
            {
                var recipients = c.Recipients;
                               
                foreach (Connection recipient in recipients)
                {
                    if (recipient == null) continue;

                    Item it = recipient.Item;
                    if (it == null) continue;

                    if (it.Condition <= 0.0f) continue;

                    foreach (Powered powered in it.GetComponents<Powered>())
                    {
                        if (powered == null || !powered.IsActive) continue;

                        if (connectedList.Contains(powered)) continue;

                        PowerTransfer powerTransfer = powered as PowerTransfer;
                        if (powerTransfer != null)
                        {
                            powerTransfer.CheckJunctions(deltaTime);
                            continue;
                        }

                        PowerContainer powerContainer = powered as PowerContainer;
                        if (powerContainer != null)
                        {
                            if (recipient.Name == "power_in")
                            {
                                fullLoad += powerContainer.CurrPowerConsumption;
                            }
                            else
                            {
                                fullPower += powerContainer.CurrPowerOutput;
                            }
                        }
                        else
                        {
                            connectedList.Add(powered);
                            //positive power consumption = the construction requires power -> increase load
                            if (powered.CurrPowerConsumption > 0.0f)
                            {
                                fullLoad += powered.CurrPowerConsumption;
                            }
                            else if (powered.CurrPowerConsumption < 0.0f)
                            //negative power consumption = the construction is a 
                            //generator/battery or another junction box
                            {
                                fullPower -= powered.CurrPowerConsumption;
                            }
                        }
                    }

                }
            }
        }
        
        public override void OnMapLoaded()
        {
            var connections = item.Connections;
            if (connections == null)
            {
                IsActive = false;
                return;
            }

            powerConnections = connections.FindAll(c => c.IsPower);
            if (powerConnections.Count == 0) IsActive = false;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);

            if (connection.Name.Length > 5 && connection.Name.Substring(0, 6).ToLowerInvariant() == "signal")
            {
                connection.SendSignal(stepsTaken, signal, source, sender, 0.0f);
            }
        }

    }
}
