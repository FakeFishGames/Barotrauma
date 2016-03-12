using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Engine : Powered
    {

        private float force;

        private float targetForce;

        private float maxForce;

        //[Editable, HasDefaultValue(1.0f, true)]
        //public float PowerPerForce
        //{
        //    get { return powerPerForce; }
        //    set
        //    {
        //        powerPerForce = Math.Max(0.0f, value);
        //    }
        //}

        [Editable, HasDefaultValue(2000.0f, true)]
        public float MaxForce
        {
            get { return maxForce; }
            set
            {
                maxForce = Math.Max(0.0f, value);
            }
        }

        public float Force
        {
            get { return force;}
            set { force = MathHelper.Clamp(value, -100.0f, 100.0f); }
        }

        public Engine(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            var button = new GUIButton(new Rectangle(160, 50, 30, 30), "-", GUI.Style, GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                targetForce -= 1.0f;
                item.NewComponentEvent(this, true, false);

                return true;
            };

            button = new GUIButton(new Rectangle(200, 50, 30, 30), "+", GUI.Style, GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                targetForce += 1.0f;
                item.NewComponentEvent(this, true, false);

                return true;
            };   
        }

        public float CurrentVolume
        {
            get { return Math.Abs((force / 100.0f) * (voltage / minVoltage)); }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            currPowerConsumption = Math.Abs(targetForce)/100.0f * powerConsumption;

            if (powerConsumption == 0.0f) voltage = 1.0f;

            Force = MathHelper.Lerp(force, (voltage < minVoltage) ? 0.0f : targetForce, 0.1f);
            if (Math.Abs(Force) > 1.0f)
            {
                Vector2 currForce = new Vector2((force / 100.0f) * maxForce * (voltage / minVoltage), 0.0f);

                Submarine.Loaded.ApplyForce(currForce);

                if (item.CurrentHull != null)
                {
                    item.CurrentHull.AiTarget.SoundRange = Math.Max(currForce.Length(), item.CurrentHull.AiTarget.SoundRange);
                }

                for (int i = 0; i < 5; i++)
                {
                    GameMain.ParticleManager.CreateParticle("bubbles", item.WorldPosition - (Vector2.UnitX * item.Rect.Width/2),
                        -currForce / 5.0f + new Vector2(Rand.Range(-100.0f, 100.0f), Rand.Range(-50f, 50f)),
                        0.0f, item.CurrentHull);
                }
            }

            voltage = 0.0f;
        }
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //isActive = true;
            GuiFrame.Draw(spriteBatch);
            
            //int width = 300, height = 300;
            //int x = Game1.GraphicsWidth / 2 - width / 2;
            //int y = Game1.GraphicsHeight / 2 - height / 2 - 50;

            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            spriteBatch.DrawString(GUI.Font, "Force: " + (int)(targetForce) + " %", new Vector2(GuiFrame.Rect.X + 30, GuiFrame.Rect.Y + 30), Color.White);
      
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            force = MathHelper.Lerp(force, 0.0f, 0.1f);
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            base.ReceiveSignal(signal, connection, sender, power);

            if (connection.Name == "set_force")
            {
                float tempForce;
                if (float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out tempForce))
                {
                    targetForce = tempForce;
                    targetForce = MathHelper.Clamp(targetForce, -100.0f, 100.0f);
                }
            }  
        }
    }
}
