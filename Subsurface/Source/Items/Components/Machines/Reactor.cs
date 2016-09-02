using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    class Reactor : Powered, IDrawableComponent
    {
        const float NetworkUpdateInterval = 3.0f;

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

        private GUITickBox autoTempTickBox;

        private bool unsentChanges;
        private float sendUpdateTimer;

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
                if (autoTempTickBox!=null) autoTempTickBox.Selected = value;
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

            var button = new GUIButton(new Rectangle(410, 70, 40, 40), "-", GUI.Style, GuiFrame);
            button.OnPressed = () =>
            {
                unsentChanges = true;
                ShutDownTemp -= 100.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(460, 70, 40,40), "+", GUI.Style, GuiFrame);
            button.OnPressed = () =>
            {
                unsentChanges = true;
                ShutDownTemp += 100.0f;

                return false;
            };

            autoTempTickBox = new GUITickBox(new Rectangle(410, 170, 20, 20), "Automatic temperature control", Alignment.TopLeft, GuiFrame);
            autoTempTickBox.OnSelected = ToggleAutoTemp;

            button = new GUIButton(new Rectangle(210, 290, 40, 40), "+", GUI.Style, GuiFrame);
            button.OnPressed = () =>
            {
                unsentChanges = true;
                FissionRate += 1.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(210, 340, 40, 40), "-", GUI.Style, GuiFrame);
            button.OnPressed = () =>
            {
                unsentChanges = true;
                FissionRate -= 1.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(500, 290, 40, 40), "+", GUI.Style, GuiFrame);
            button.OnPressed = () =>
            {
                unsentChanges = true;
                CoolingRate += 1.0f;

                return false;
            };

            button = new GUIButton(new Rectangle(500, 340, 40, 40), "-", GUI.Style, GuiFrame);
            button.OnPressed = () =>
            {
                unsentChanges = true;
                CoolingRate -= 1.0f;

                return false;
            };
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            
            fissionRate = Math.Min(fissionRate, AvailableFuel);
            
            float heat = 80 * fissionRate * (AvailableFuel/2000.0f);
            float heatDissipation = 50 * coolingRate + Math.Max(ExtraCooling, 5.0f);

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

            //fission rate can't be lowered below a certain amount if the core is too hot
            //FissionRate = Math.Max(fissionRate, heat / 200.0f);
            
            //the power generated by the reactor is equal to the temperature
            currPowerConsumption = -temperature*powerPerTemp;

            //foreach (Item i in item.ContainedItems)
            //{
            //    i.Condition = 5.0f;
            //}

            if (item.CurrentHull != null)
            {
                //the sound can be heard from 20 000 display units away when everything running at 100%
                item.CurrentHull.SoundRange = Math.Max((coolingRate + fissionRate) * 100, item.CurrentHull.AiTarget.SoundRange);
            }

            UpdateGraph(deltaTime);

            ExtraCooling = 0.0f;
            AvailableFuel = 0.0f;

            item.SendSignal(0, ((int)temperature).ToString(), "temperature_out");
              
            sendUpdateTimer = Math.Max(sendUpdateTimer - deltaTime, 0.0f);

            if (unsentChanges && sendUpdateTimer<= 0.0f)
            {
                item.NewComponentEvent(this, true, true);
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

            GameServer.Log("Reactor meltdown!", Color.Red);
 
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

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.Rect.X + item.Rect.Width / 2 - 6, -item.Rect.Y + 29),
                new Vector2(12, 42), Color.Black);

            if (temperature > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.Rect.X + item.Rect.Width / 2 - 5, -item.Rect.Y + 30 + (40.0f * (1.0f - temperature / 10000.0f))),
                    new Vector2(10, 40 * (temperature / 10000.0f)), new Color(temperature / 10000.0f, 1.0f - (temperature / 10000.0f), 0.0f, 1.0f), true);
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
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            IsActive = true;
            
            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Update(1.0f / 60.0f);
            GuiFrame.Draw(spriteBatch);

            float xOffset = (graphTimer / (float)updateGraphInterval);

            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            spriteBatch.DrawString(GUI.Font, "Output: " + (int)temperature + " kW", 
                new Vector2(x + 450, y + 30), Color.Red);
            spriteBatch.DrawString(GUI.Font, "Grid load: " + (int)load + " kW",
                new Vector2(x + 600, y + 30), Color.Yellow);

            float maxLoad = 0.0f;
            foreach (float loadVal in loadGraph)
            {
                maxLoad = Math.Max(maxLoad, loadVal);
            }

            DrawGraph(tempGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 400, 250), Math.Max(10000.0f, maxLoad), xOffset, Color.Red);

            DrawGraph(loadGraph, spriteBatch,
                new Rectangle(x + 30, y + 30, 400, 250),  Math.Max(10000.0f, maxLoad), xOffset, Color.Yellow);

            spriteBatch.DrawString(GUI.Font, "Shutdown Temperature: " + (int)shutDownTemp, new Vector2(x + 450, y + 80), Color.White);

            //spriteBatch.DrawString(GUI.Font, "Automatic Temperature Control: " + ((autoTemp) ? "ON" : "OFF"), new Vector2(x + 450, y + 180), Color.White);
            
            y += 300;

            spriteBatch.DrawString(GUI.Font, "Fission rate: " + (int)fissionRate + " %", new Vector2(x + 30, y), Color.White);
            DrawGraph(fissionRateGraph, spriteBatch, 
                new Rectangle(x + 30, y + 30, 200, 100), 100.0f, xOffset, Color.Orange);


            spriteBatch.DrawString(GUI.Font, "Cooling rate: " + (int)coolingRate + " %", new Vector2(x + 320, y), Color.White);
            DrawGraph(coolingRateGraph, spriteBatch,
                new Rectangle(x + 320, y + 30, 200, 100), 100.0f, xOffset, Color.LightBlue);


            //y = y - 260;
        }

        private bool ToggleAutoTemp(GUITickBox tickBox)
        {
            unsentChanges = true;
            autoTemp = tickBox.Selected;

            return true;
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

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power)
        {
            switch (connection.Name)
            {
                case "shutdown":
                    shutDownTemp = 0.0f;
                    break;
            }
        }

        public override void ClientWrite(NetOutgoingMessage msg)
        {
            msg.Write(autoTemp);
            msg.WriteRangedSingle(shutDownTemp, 0.0f, 10000.0f, 8);

            msg.WriteRangedSingle(coolingRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(fissionRate, 0.0f, 100.0f, 8);
        }

        public override void ServerRead(NetIncomingMessage msg)
        {
            autoTemp = msg.ReadBoolean();
            ShutDownTemp = msg.ReadRangedSingle(0.0f, 10000.0f, 8);

            CoolingRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            FissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        }

        public override void ServerWrite(NetOutgoingMessage msg)
        {
            msg.WriteRangedSingle(temperature, 0.0f, 10000.0f, 16);

            msg.Write(autoTemp);
            msg.WriteRangedSingle(shutDownTemp, 0.0f, 10000.0f, 8);

            msg.WriteRangedSingle(coolingRate, 0.0f, 100.0f, 8);
            msg.WriteRangedSingle(fissionRate, 0.0f, 100.0f, 8);
        }

        public override void ClientRead(NetIncomingMessage msg)
        {
            Temperature = msg.ReadRangedSingle(0.0f, 10000.0f, 16);

            autoTemp = msg.ReadBoolean();
            ShutDownTemp = msg.ReadRangedSingle(0.0f, 10000.0f, 8);

            CoolingRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            FissionRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        }
        
    }
}
