using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Engine : Powered
    {
        private float force;

        private float targetForce;

        private float maxForce;
        
        [Editable(0.0f, 10000000.0f, ToolTip = "The amount of force exerted on the submarine when the engine is operating at full power."), 
        Serialize(2000.0f, true)]
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

#if CLIENT
            var button = new GUIButton(new Rectangle(160, 50, 30, 30), "-", "", GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                targetForce -= 1.0f;
                
                return true;
            };

            button = new GUIButton(new Rectangle(200, 50, 30, 30), "+", "", GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                targetForce += 1.0f;
                
                return true;
            };
#endif
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

                item.Submarine.ApplyForce(currForce);

                if (item.CurrentHull != null)
                {
                    item.CurrentHull.AiTarget.SoundRange = Math.Max(currForce.Length(), item.CurrentHull.AiTarget.SoundRange);
                }

#if CLIENT
                for (int i = 0; i < 5; i++)
                {
                    GameMain.ParticleManager.CreateParticle("bubbles", item.WorldPosition - (Vector2.UnitX * item.Rect.Width/2),
                        -currForce / 5.0f + new Vector2(Rand.Range(-100.0f, 100.0f), Rand.Range(-50f, 50f)),
                        0.0f, item.CurrentHull);
                }
#endif
            }

            voltage = 0.0f;
        }
        
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            force = MathHelper.Lerp(force, 0.0f, 0.1f);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);

            if (connection.Name == "set_force")
            {
                float tempForce;
                if (float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out tempForce))
                {
                    targetForce = MathHelper.Clamp(tempForce, -100.0f, 100.0f);
                }
            }  
        }
    }
}
