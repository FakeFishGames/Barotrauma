using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Pump : Powered
    {
        float flowPercentage;
        float maxFlow;

        float? targetLevel;

        Hull hull1, hull2;

        [HasDefaultValue(0.0f, true)]
        private float FlowPercentage
        {
            get { return flowPercentage; }
            set 
            {
                if (float.IsNaN(flowPercentage)) return;
                flowPercentage = MathHelper.Clamp(value,-100.0f,100.0f); 
            }
        }

        [HasDefaultValue(100.0f, false)]
        public float MaxFlow
        {
            get { return maxFlow; }
            set { maxFlow = value; } 
        }

        public Pump(Item item, XElement element)
            : base(item, element)
        {
            item.linkedTo.CollectionChanged += delegate(object sender, NotifyCollectionChangedEventArgs e)
            { GetHulls(); };
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (targetLevel != null)
            {
                float hullPercentage = 0.0f;
                if (hull1 != null) hullPercentage = (hull1.Volume / hull1.FullVolume) * 100.0f;
                flowPercentage = ((float)targetLevel - hullPercentage);
            }


            currPowerConsumption = powerConsumption * Math.Abs(flowPercentage / 100.0f);

            if (voltage < minVoltage) return;

            if (hull2 == null && hull1 == null) return;
            
            float powerFactor = (currPowerConsumption==0.0f) ? 1.0f : voltage;
            //flowPercentage = maxFlow * powerFactor;

            float deltaVolume = 0.0f;

                deltaVolume = (flowPercentage/100.0f) * maxFlow * powerFactor;
            

            hull1.Volume += deltaVolume;
            if (hull1.Volume > hull1.FullVolume) hull1.Pressure += 0.5f;

            if (hull2 != null)
            {
                hull2.Volume -= deltaVolume;
                if (hull2.Volume > hull1.FullVolume) hull2.Pressure += 0.5f;
            }

            voltage = 0.0f;
        }
        
        private void GetHulls()
        {
            hull1 = null;
            hull2 = null;

            foreach (MapEntity e in item.linkedTo)
            {
                Hull hull = e as Hull;
                if (hull == null) continue;

                if (hull1 == null)
                {
                    hull1 = hull;
                }
                else if (hull2 == null && hull != hull1)
                {
                    hull2 = hull;
                    break;
                }
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 20, y + 20, 100, 40), ((isActive) ? "TURN OFF" : "TURN ON")))
            {
                targetLevel = null;
                isActive = !isActive;
                if (!isActive) currPowerConsumption = 0.0f;
                item.NewComponentEvent(this, true);
            }
            
            spriteBatch.DrawString(GUI.Font, "Flow percentage: " + (int)flowPercentage + " %", new Vector2(x + 20, y + 80), Color.White);

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 200, y + 70, 40, 40), "+", true))
            {
                FlowPercentage += 1.0f;
                item.NewComponentEvent(this, true);
            }
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 250, y + 70, 40, 40), "-", true))
            {
                FlowPercentage -= 1.0f;
                item.NewComponentEvent(this, true);
            }


            
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            base.ReceiveSignal(signal, connection, sender, power);
            
            if (connection.Name == "toggle")
            {
                isActive = !isActive;
            }
            else if (connection.Name == "set_active")
            {
                isActive = (signal != "0");                
            }
            else if (connection.Name == "set_speed")
            {
                float tempSpeed;
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out tempSpeed))
                {
                    flowPercentage = MathHelper.Clamp(tempSpeed, -100.0f, 100.0f);
                }
            }
            else if (connection.Name == "set_targetlevel")
            {
                float tempTarget;
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out tempTarget))
                {
                    targetLevel = MathHelper.Clamp(tempTarget, 0.0f, 100.0f);
                }
            }

            if (!isActive) currPowerConsumption = 0.0f;
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(flowPercentage);
            message.Write(isActive);
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            float newFlow = 0.0f;
            bool newActive;

            try
            {
                newFlow = message.ReadFloat();
                newActive = message.ReadBoolean();
            }

            catch { return; }

            FlowPercentage = newFlow;
            isActive = newActive;
        }
    }
}
