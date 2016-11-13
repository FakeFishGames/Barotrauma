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
        private float flowPercentage;
        private float maxFlow;

        private float? targetLevel;

        private float lastUpdate;

        public Hull hull1;

        private GUITickBox isActiveTickBox;

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

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }
            set
            {
                base.IsActive = value;

                if (isActiveTickBox != null) isActiveTickBox.Selected = value;
            }
        }

        public Pump(Item item, XElement element)
            : base(item, element)
        {
            GetHull();

            isActiveTickBox = new GUITickBox(new Rectangle(0, 0, 20, 20), "Running", Alignment.TopLeft, GuiFrame);
            isActiveTickBox.OnSelected = (GUITickBox box) =>
            {
                targetLevel = null;
                IsActive = !IsActive;
                if (!IsActive) currPowerConsumption = 0.0f;

                return true;
            };

            var button = new GUIButton(new Rectangle(160, 40, 35, 30), "OUT", GUI.Style, GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                FlowPercentage -= 10.0f;

                return true;
            };

            button = new GUIButton(new Rectangle(210, 40, 35, 30), "IN", GUI.Style, GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                FlowPercentage += 10.0f;

                return true;
            };     
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
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);
            
            spriteBatch.DrawString(GUI.Font, "Pumping speed: " + (int)flowPercentage + " %", new Vector2(x + 40, y + 85), Color.White);
     
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, sender, power);
            
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

        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            //flowpercentage can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger(-10, 10, (int)(flowPercentage / 10.0f));
            msg.Write(IsActive);
        }

        public void ServerRead(Lidgren.Network.NetIncomingMessage msg, Barotrauma.Networking.Client c)
        {
            FlowPercentage = msg.ReadRangedInteger(-10, 10) * 10.0f;
            IsActive = msg.ReadBoolean();
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c, object[] extraData = null)
        {
            //flowpercentage can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger(-10, 10, (int)(flowPercentage / 10.0f));
            msg.Write(IsActive);
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage msg, float sendingTime)
        {
            FlowPercentage = msg.ReadRangedInteger(-10, 10) * 10.0f;
            IsActive = msg.ReadBoolean();
        }
        
    }
}
