using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    class Reactor : Powered
    {
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

        private float lastUpdate;

        private PropertyTask powerUpTask;

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

        public float FissionRate
        {
            get { return fissionRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                fissionRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }
        
        public float CoolingRate
        {
            get { return coolingRate; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                coolingRate = MathHelper.Clamp(value, 0.0f, 100.0f); 
            }
        }

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

        public bool AutoTemp
        {
            get { return autoTemp; }
            set { autoTemp = value; }
        }

        public float ExtraCooling { get; set; }

        public float AvailableFuel { get; set; }

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
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            
            fissionRate = Math.Min(fissionRate, AvailableFuel);
            
            float heat = 100 * fissionRate * (AvailableFuel/2000.0f);
            float heatDissipation = 50 * coolingRate + ExtraCooling;

            float deltaTemp = (((heat - heatDissipation) * 5) - temperature) / 10000.0f;
            Temperature = temperature + deltaTemp;

            if (temperature>fireTemp && temperature-deltaTemp<fireTemp)
            {
                Vector2 baseVel = Rand.Vector(300.0f);
                for (int i = 0; i < 10; i++)
                {
                    var particle = GameMain.ParticleManager.CreateParticle("spark", item.WorldPosition,
                        baseVel + Rand.Vector(100.0f), 0.0f, item.CurrentHull);

                    if (particle != null) particle.Size *= Rand.Range(0.5f, 1.0f);
                }

                new FireSource(item.Position);
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
                fissionRate = Math.Min(load / 200.0f, shutDownTemp);
                //float target = Math.Min(targetTemp, load);
                CoolingRate = coolingRate + (temperature - Math.Min(load, shutDownTemp) + deltaTemp)*0.1f;
            }

            //fission rate can't be lowered below a certain amount if the core is too hot
            FissionRate = Math.Max(fissionRate, heat / 200.0f);
            
            //the power generated by the reactor is equal to the temperature
            currPowerConsumption = -temperature*powerPerTemp;

            //foreach (Item i in item.ContainedItems)
            //{
            //    i.Condition = 5.0f;
            //}

            if (item.CurrentHull != null)
            {
                //the sound can be heard from 20 000 display units away when everything running at 100%
                item.CurrentHull.SoundRange = (coolingRate + fissionRate) * 100;
            }

            UpdateGraph(deltaTime);

            ExtraCooling = 0.0f;
            AvailableFuel = 0.0f;

            item.SendSignal(((int)temperature).ToString(), "temperature_out");
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);

            Temperature -= deltaTime * 1000.0f;
            FissionRate -= deltaTime * 10.0f;
            CoolingRate -= deltaTime * 10.0f;

            currPowerConsumption = -temperature;

            UpdateGraph(deltaTime);

            ExtraCooling = 0.0f;
        }

        private void UpdateGraph(float deltaTime)
        {
            graphTimer += deltaTime * 1000.0f;

            if (graphTimer > updateGraphInterval)
            {
                UpdateGraph(fissionRateGraph, fissionRate);
                UpdateGraph(coolingRateGraph, coolingRate);
                UpdateGraph(tempGraph, temperature);

                UpdateGraph(loadGraph, load);

                graphTimer = 0.0f;
            }
        }

        private void MeltDown()
        {
            if (item.Condition <= 0.0f) return;
 
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
            return (picker != null);
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            base.Draw(spriteBatch);

            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.Rect.X + item.Rect.Width / 2 - 6, -item.Rect.Y + 29),
                new Vector2(12, 42), Color.Black);

            if (temperature > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.Rect.X + item.Rect.Width / 2 - 5, -item.Rect.Y + 30 + (40.0f * (1.0f - temperature / 10000.0f))),
                    new Vector2(10, 40 * (temperature / 10000.0f)), new Color(temperature / 10000.0f, 1.0f - (temperature / 10000.0f), 0.0f, 1.0f), true);
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjective objective)
        {
             switch (objective.Option.ToLower())
             {
                 case "power up":
                     float tempDiff = load - temperature;

                     shutDownTemp = Math.Min(load + 1000.0f, 7500.0f);

                     //temperature too high/low
                     if (Math.Abs(tempDiff)>500.0f)
                     {
                         autoTemp = false;
                         FissionRate += deltaTime * 100.0f * Math.Sign(tempDiff);
                         CoolingRate -= deltaTime * 100.0f * Math.Sign(tempDiff);
                     }
                     //temperature OK
                     else
                     {
                         autoTemp = true;
                     }

                     break;
                 case "shutdown":

                     shutDownTemp = 0.0f;

                     break;
             }

             return false;
        }
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;

            bool valueChanged = false;

            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            float xOffset = (graphTimer / (float)updateGraphInterval);

            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            spriteBatch.DrawString(GUI.Font, "Temperature: " + (int)temperature + " C", 
                new Vector2(x + 450, y + 30), Color.Red);
            spriteBatch.DrawString(GUI.Font, "Grid load: " + (int)load + " C",
                new Vector2(x + 620, y + 30), Color.Yellow);

            DrawGraph(tempGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 400, 250), 10000.0f, xOffset, Color.Red);

            DrawGraph(loadGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 400, 250), 10000.0f, xOffset, Color.Yellow);

            spriteBatch.DrawString(GUI.Font, "Shutdown Temperature: " + shutDownTemp, new Vector2(x + 450, y + 80), Color.White);
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 450, y + 110, 40, 40), "+", true))
            {
                valueChanged = true;
                ShutDownTemp += 100.0f;
            }
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 500, y + 110, 40, 40), "-", true))
            {
                valueChanged = true;
                ShutDownTemp -= 100.0f;
            }


            spriteBatch.DrawString(GUI.Font, "Automatic Temperature Control: " + ((autoTemp) ? "ON" : "OFF"), new Vector2(x + 450, y + 180), Color.White);
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 450, y + 210, 100, 40), ((autoTemp) ? "TURN OFF" : "TURN ON")))
            {
                valueChanged = true;
                autoTemp = !autoTemp;
            }



            y += 300;

            spriteBatch.DrawString(GUI.Font, "Fission rate: " + (int)fissionRate + " %", new Vector2(x + 30, y), Color.White);
            DrawGraph(fissionRateGraph, spriteBatch, 
                new Rectangle(x + 30, y + 30, 200, 100), 100.0f, xOffset, Color.Orange);

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 250, y + 30, 40, 40), "+", true))
            {
                valueChanged = true;
                FissionRate += 1.0f;
            }
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 250, y + 80, 40, 40), "-", true))
            {
                valueChanged = true;
                FissionRate -= 1.0f;
            }

            spriteBatch.DrawString(GUI.Font, "Cooling rate: " + (int)coolingRate + " %", new Vector2(x + 320, y), Color.White);
            DrawGraph(coolingRateGraph, spriteBatch,
                new Rectangle(x + 320, y + 30, 200, 100), 100.0f, xOffset, Color.LightBlue);

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 540, y + 30, 40, 40), "+", true))
            {
                valueChanged = true;
                CoolingRate += 1.0f;
            }
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 540, y + 80, 40, 40), "-", true))
            {
                valueChanged = true;
                CoolingRate -= 1.0f;
            }

            //y = y - 260;

            if (valueChanged)
            {
                item.NewComponentEvent(this, true, false);
            }
        }

        static void UpdateGraph<T>(IList<T> graph, T newValue)
        {
            for (int i = graph.Count - 1; i > 0; i--)
            {
                graph[i] = graph[i - 1];
            }
            graph[0] = newValue;
        }

        static void DrawGraph(IList<float> graph, SpriteBatch spriteBatch, Rectangle rect, float maxVal, float xOffset, Color color)
        {
            float lineWidth = (float)rect.Width / (float)(graph.Count - 2);
            float yScale = (float)rect.Height / maxVal;

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (graph[1] + (graph[0] - graph[1]) * xOffset) * yScale);

            float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);

            for (int i = 1; i < graph.Count - 1; i++)
            {
                currX -= lineWidth;

                Vector2 newPoint = new Vector2(currX, rect.Bottom - graph[i] * yScale);

                GUI.DrawLine(spriteBatch, prevPoint, newPoint - new Vector2(1.0f, 0), color);

                prevPoint = newPoint;
            }

            Vector2 lastPoint = new Vector2(rect.X,
                rect.Bottom - (graph[graph.Count - 1] + (graph[graph.Count - 2] - graph[graph.Count - 1]) * xOffset) * yScale);

            GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color);
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power)
        {
            switch (connection.Name)
            {
                case "shutdown":
                    shutDownTemp = 0.0f;
                    break;
            }
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message)
        {
            message.Write(autoTemp);
            message.WriteRangedSingle(temperature, 0.0f, 10000.0f, 16);
            message.WriteRangedSingle(shutDownTemp, 0.0f, 10000.0f, 8);

            message.WriteRangedSingle(coolingRate, 0.0f, 100.0f, 8);
            message.WriteRangedSingle(fissionRate, 0.0f, 100.0f, 8);

            return true;
        }

        public override void ReadNetworkData(NetworkEventType type, NetBuffer message, float sendingTime)
        {
            if (sendingTime < lastUpdate) return;

            bool newAutoTemp;
            float newTemperature, newShutDownTemp;
            float newCoolingRate, newFissionRate;

            try
            {
                newAutoTemp = message.ReadBoolean();
                newTemperature = message.ReadRangedSingle(0.0f, 10000.0f, 16);
                newShutDownTemp = message.ReadRangedSingle(0.0f, 10000.0f, 8);
                newShutDownTemp = MathUtils.RoundTowardsClosest(newShutDownTemp, 100.0f);

                newCoolingRate = message.ReadRangedSingle(0.0f, 100.0f, 8);
                newFissionRate = message.ReadRangedSingle(0.0f, 100.0f, 8);
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("invalid network message", e);
#endif
                return; 
            }

            autoTemp = newAutoTemp;
            Temperature = newTemperature;
            ShutDownTemp = newShutDownTemp;

            CoolingRate = newCoolingRate;
            FissionRate = newFissionRate;

            lastUpdate = sendingTime;
        }
    }
}
