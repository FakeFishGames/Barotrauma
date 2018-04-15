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

        private SpriteSheet propellerSprite;

        private Attack propellerDamage;

        private float damageTimer;
        
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

        [Editable, Serialize("0.0,0.0", true)]
        public Vector2 PropellerPos
        {
            get;
            set;
        }

        public float Force
        {
            get { return force;}
            set { force = MathHelper.Clamp(value, -100.0f, 100.0f); }
        }

        public float CurrentVolume
        {
            get { return Math.Abs((force / 100.0f) * (voltage / minVoltage)); }
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

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "propellersprite":
                        propellerSprite = new SpriteSheet(subElement);
                        AnimSpeed = subElement.GetAttributeFloat("animspeed", 1.0f);
                        break;
                    case "propellerdamage":
                        propellerDamage = new Attack(subElement);
                        break;
                }
            }
#endif
        }
    
        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);

            UpdateAnimation(deltaTime);

            currPowerConsumption = Math.Abs(targetForce) / 100.0f * powerConsumption;
            if (item.IsOptimized("electrical")) currPowerConsumption *= 0.5f;

            if (powerConsumption == 0.0f) voltage = 1.0f;
            
            Force = MathHelper.Lerp(force, (voltage < minVoltage) ? 0.0f : targetForce, 0.1f);
            if (Math.Abs(Force) > 1.0f)
            {
                Vector2 currForce = new Vector2((force / 100.0f) * maxForce * (voltage / minVoltage), 0.0f);
                if (item.IsOptimized("mechanical")) currForce *= 1.5f;

                item.Submarine.ApplyForce(currForce);

                UpdatePropellerDamage(deltaTime);

                if (item.CurrentHull != null)
                {
                    item.CurrentHull.AiTarget.SoundRange = Math.Max(currForce.Length(), item.CurrentHull.AiTarget.SoundRange);
                }

#if CLIENT
                for (int i = 0; i < 5; i++)
                {
                    GameMain.ParticleManager.CreateParticle("bubbles", item.WorldPosition + PropellerPos,
                        -currForce / 5.0f + new Vector2(Rand.Range(-100.0f, 100.0f), Rand.Range(-50f, 50f)),
                        0.0f, item.CurrentHull);
                }
#endif
            }

            voltage = 0.0f;
        }

        private void UpdatePropellerDamage(float deltaTime)
        {
            damageTimer += deltaTime;
            if (damageTimer < 0.5f) return;
            damageTimer = 0.1f;

            if (propellerDamage == null) return;
            Vector2 propellerWorldPos = item.WorldPosition + PropellerPos;
            foreach (Character character in Character.CharacterList)
            {
                if (character.Submarine != null || !character.Enabled || character.Removed) continue;

                float dist = Vector2.DistanceSquared(character.WorldPosition, propellerWorldPos);
                if (dist > propellerDamage.DamageRange * propellerDamage.DamageRange) continue;

                propellerDamage.DoDamage(null, character, propellerWorldPos, 1.0f, true);
            }
        }

        partial void UpdateAnimation(float deltaTime);
        
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
