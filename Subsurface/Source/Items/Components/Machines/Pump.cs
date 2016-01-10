using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Pump : Powered
    {
        float flowPercentage;
        float maxFlow;

        float? targetLevel;

        float lastUpdate;

        Hull hull1;

        [HasDefaultValue(0.0f, true)]
        public float FlowPercentage
        {
            get { return flowPercentage; }
            set 
            {
                if (!MathUtils.IsValid(flowPercentage)) return;
                flowPercentage = MathHelper.Clamp(value,-100.0f,100.0f);
                flowPercentage = MathUtils.Round(flowPercentage, 1.0f);
            }
        }

        [HasDefaultValue(80.0f, false)]
        public float MaxFlow
        {
            get { return maxFlow; }
            set { maxFlow = value; } 
        }

        float currFlow;
        public float CurrFlow
        {
            get 
            {
                if (!IsActive) return 0.0f;
                return Math.Abs(currFlow); 
            }
        }

        public Pump(Item item, XElement element)
            : base(item, element)
        {
            GetHull();
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            GetHull();
        }

        public override void OnMapLoaded()
        {
            GetHull();
        }

        public override void Update(float deltaTime, Camera cam)
        {
            currFlow = 0.0f;

            if (targetLevel != null)
            {
                float hullPercentage = 0.0f;
                if (hull1 != null) hullPercentage = (hull1.Volume / hull1.FullVolume) * 100.0f;
                FlowPercentage = ((float)targetLevel - hullPercentage) * 10.0f;
            }

            currPowerConsumption = powerConsumption * Math.Abs(flowPercentage / 100.0f);

            if (voltage < minVoltage) return;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (hull1 == null) return;
            
            float powerFactor = (currPowerConsumption==0.0f) ? 1.0f : voltage;
            //flowPercentage = maxFlow * powerFactor;

            currFlow = (flowPercentage / 100.0f) * maxFlow * powerFactor;

            hull1.Volume += currFlow;
            if (hull1.Volume > hull1.FullVolume) hull1.Pressure += 0.5f;

            //if (hull2 != null)
            //{
            //    hull2.Volume -= currFlow;
            //    if (hull2.Volume > hull1.FullVolume) hull2.Pressure += 0.5f;
            //}

            voltage = 0.0f;
        }

        private void GetHull()
        {
            hull1 = Hull.FindHull(item.WorldPosition, item.CurrentHull);
        }
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 20, y + 20, 100, 40), ((IsActive) ? "TURN OFF" : "TURN ON")))
            {
                targetLevel = null;
                IsActive = !IsActive;
                if (!IsActive) currPowerConsumption = 0.0f;
                item.NewComponentEvent(this, true, true);
            }
            
            spriteBatch.DrawString(GUI.Font, "Pumping speed: " + (int)flowPercentage + " %", new Vector2(x + 20, y + 80), Color.White);
            
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 200, y + 70, 40, 40), "OUT", false))
            {
                FlowPercentage -= 10.0f;
                item.NewComponentEvent(this, true, true);
            }
            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 250, y + 70, 40, 40), "IN", false))
            {
                FlowPercentage += 10.0f;
                item.NewComponentEvent(this, true, true);
            }            
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            base.ReceiveSignal(signal, connection, sender, power);
            
            if (connection.Name == "toggle")
            {
                IsActive = !IsActive;
            }
            else if (connection.Name == "set_active")
            {
                IsActive = (signal != "0");                
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
                    targetLevel = MathHelper.Clamp((tempTarget+100.0f)/2.0f, 0.0f, 100.0f);
                }
            }

            if (!IsActive) currPowerConsumption = 0.0f;
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            message.WriteRangedInteger(-10,10,(int)(flowPercentage/10.0f));
            message.Write(IsActive);
            message.WritePadBits();

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message, float sendingTime)
        {
            float newFlow = 0.0f;
            bool newActive;

            if (sendingTime < lastUpdate) return;

            try
            {
                newFlow = message.ReadRangedInteger(-10,10)*10.0f;
                newActive = message.ReadBoolean();
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("invalid network message", e);
#endif
                return;
            }

            FlowPercentage = newFlow;
            IsActive = newActive;

            lastUpdate = sendingTime;
        }
    }
}
