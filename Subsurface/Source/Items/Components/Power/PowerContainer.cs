using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    class PowerContainer : Powered, IDrawableComponent
    {
        //[power/min]        
        float capacity;

        float charge;

        float rechargeVoltage, outputVoltage;

        //how fast the battery can be recharged
        float maxRechargeSpeed;

        //how fast it's currently being recharged (can be changed, so that
        //charging can be slowed down or disabled if there's a shortage of power)
        float rechargeSpeed;

        float maxOutput;

        public float CurrPowerOutput
        {
            get;
            private set;
        }

        [Editable, HasDefaultValue(10.0f, true)]
        public float MaxOutPut
        {
            set { maxOutput = value; }
            get { return maxOutput; }
        }

        [HasDefaultValue(10.0f, true), Editable]
        public float Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1.0f); }
        }

        [Editable, HasDefaultValue(0.0f, true)]
        public float Charge
        {
            get { return charge; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                charge = MathHelper.Clamp(value, 0.0f, capacity); 
            }
        }

        [HasDefaultValue(10.0f, true), Editable]
        public float RechargeSpeed
        {
            get { return rechargeSpeed; }
            set
            {
                if (!MathUtils.IsValid(value)) return;              
                rechargeSpeed = MathHelper.Clamp(value, 0.0f, maxRechargeSpeed);
                rechargeSpeed = MathUtils.Round(rechargeSpeed, Math.Max(maxRechargeSpeed * 0.1f, 1.0f));
            }
        }

        [HasDefaultValue(10.0f, false), Editable]
        public float MaxRechargeSpeed
        {
            get { return maxRechargeSpeed; }
            set { maxRechargeSpeed = Math.Max(value, 1.0f); }
        }

        public PowerContainer(Item item, XElement element)
            : base(item, element)
        {
            //capacity = ToolBox.GetAttributeFloat(element, "capacity", 10.0f);
            //maxRechargeSpeed = ToolBox.GetAttributeFloat(element, "maxinput", 10.0f);
            //maxOutput = ToolBox.GetAttributeFloat(element, "maxoutput", 10.0f);
            
            IsActive = true;

            if (canBeSelected)
            {
                var button = new GUIButton(new Rectangle(160, 50, 30,30), "-", GUI.Style, GuiFrame);
                button.OnClicked = (GUIButton btn, object obj) =>
                {
                    RechargeSpeed = Math.Max(rechargeSpeed - maxRechargeSpeed * 0.1f, 0.0f);
                    return true;
                };

                button = new GUIButton(new Rectangle(200, 50, 30, 30), "+", GUI.Style, GuiFrame);
                button.OnClicked = (GUIButton btn, object obj) =>
                {
                    RechargeSpeed = Math.Max(rechargeSpeed + maxRechargeSpeed * 0.1f, 0.0f);
                    return true;
                };
            }
        }

        public override bool Pick(Character picker)
        {
            if (picker == null) return false;

            //picker.SelectedConstruction = (picker.SelectedConstruction == item) ? null : item;
            
            return true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            float chargeRatio = (float)(Math.Sqrt(charge / capacity));
            float gridPower = 0.0f;
            float gridLoad = 0.0f;

            //if (item.linkedTo.Count == 0) return;

            foreach (Connection c in item.Connections)
            {
                if (c.Name == "power_in") continue;
                foreach (Connection c2 in c.Recipients)
                {
                    PowerTransfer pt = c2.Item.GetComponent<PowerTransfer>();
                    if (pt == null || !pt.IsActive) continue;

                    gridLoad += pt.PowerLoad;
                    gridPower -= pt.CurrPowerConsumption;
                }
            }


            //float gridRate = voltage;

            if (chargeRatio > 0.0f)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }

            //recharge
            //if (gridRate >= chargeRate)
            //{
            if (charge >= capacity)
            {
                rechargeVoltage = 0.0f;
                charge = capacity;

                CurrPowerConsumption = 0.0f;
            }
            else
            {
                currPowerConsumption = MathHelper.Lerp(currPowerConsumption, rechargeSpeed, 0.05f);
                Charge += currPowerConsumption * rechargeVoltage / 3600.0f;
            }

            //}

            //provide power to the grid
            if (gridLoad > 0.0f)
            {
                if (charge <= 0.0f)
                {
                    CurrPowerOutput = 0.0f;
                    charge = 0.0f;
                    return;
                }

                if (gridPower < gridLoad)
                {
                    CurrPowerOutput = MathHelper.Lerp(
                       CurrPowerOutput,
                       Math.Min(maxOutput * chargeRatio, gridLoad),
                       deltaTime);
                }
                else
                {
                    CurrPowerOutput = MathHelper.Lerp(CurrPowerOutput, 0.0f, deltaTime);
                }

                Charge -= CurrPowerOutput / 3600.0f;
            }

            rechargeVoltage = 0.0f;
            outputVoltage = 0.0f;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            RechargeSpeed = maxRechargeSpeed * 0.5f;

            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power)
        {
            if (!connection.IsPower) return;

            if (connection.Name == "power_in")
            {
                rechargeVoltage = power;
            }
            else
            {
                outputVoltage = power;
            }
            //if (currPowerConsumption == 0.0f) voltage = 0.0f;
            //if (connection.IsPower) voltage = power;    
        }
        
        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.DrawPosition.X- 4, -item.DrawPosition.Y),
                new Vector2(8, 22), Color.Black);

            if (charge > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.DrawPosition.X - 3, -item.DrawPosition.Y + 1 + (20.0f * (1.0f - charge / capacity))),
                    new Vector2(6, 20 * (charge / capacity)), Color.Green, true);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);

            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;
            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            GUI.Font.DrawString(spriteBatch,
                "Charge: " + (int)charge + "/" + (int)capacity + " kWm (" + (int)((charge / capacity) * 100.0f) + " %)",
                new Vector2(x + 30, y + 30), Color.White);

            GUI.Font.DrawString(spriteBatch, "Recharge rate: " + (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", new Vector2(x + 30, y + 95), Color.White);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
        }

        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            float chargeSpeed = MathHelper.Clamp(rechargeSpeed / MaxRechargeSpeed, 0.0f, 1.0f);
            msg.WriteRangedSingle(chargeSpeed, 0.0f, 1.0f, 8);
        }

        public void ServerRead(Lidgren.Network.NetIncomingMessage msg, Barotrauma.Networking.Client c)
        {
            RechargeSpeed = msg.ReadRangedSingle(0.0f, 1.0f, 8) * maxRechargeSpeed;
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c, object[] extraData = null)
        {
            float chargeSpeed = MathHelper.Clamp(rechargeSpeed / MaxRechargeSpeed, 0.0f, 1.0f);
            msg.WriteRangedSingle(chargeSpeed, 0.0f, 1.0f, 8);

            float chargeRatio = MathHelper.Clamp(charge / capacity, 0.0f, 1.0f);
            msg.WriteRangedSingle(chargeRatio, 0.0f, 1.0f, 8);
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage msg, float sendingTime)
        {
            RechargeSpeed = msg.ReadRangedSingle(0.0f, 1.0f, 8) * maxRechargeSpeed;

            Charge = msg.ReadRangedSingle(0.0f, 1.0f, 8) * capacity;
        }        
    }
}
