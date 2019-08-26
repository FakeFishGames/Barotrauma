using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Engine : Powered, IServerSerializable, IClientSerializable
    {
        private float force;

        private float targetForce;

        private float maxForce;
        
        private Attack propellerDamage;

        private float damageTimer;

        private bool hasPower;

        private float prevVoltage;
        
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
            get { return Math.Abs((force / 100.0f) * (minVoltage <= 0.0f ? 1.0f : Math.Min(prevVoltage / minVoltage, 1.0f))); }
        }

        public Engine(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "propellerdamage":
                        propellerDamage = new Attack(subElement, item.Name + ", Engine");
                        break;
                }
            }

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);
    
        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);

            UpdateAnimation(deltaTime);

            currPowerConsumption = Math.Abs(targetForce) / 100.0f * powerConsumption;
            //pumps consume more power when in a bad condition
            currPowerConsumption *= MathHelper.Lerp(2.0f, 1.0f, item.Condition / item.MaxCondition);

            if (powerConsumption == 0.0f) voltage = 1.0f;

            prevVoltage = voltage;
            hasPower = voltage > minVoltage;

            Force = MathHelper.Lerp(force, (voltage < minVoltage) ? 0.0f : targetForce, 0.1f);
            if (Math.Abs(Force) > 1.0f)
            {
                Vector2 currForce = new Vector2((force / 10.0f) * maxForce * Math.Min(voltage / minVoltage, 1.0f), 0.0f);
                //less effective when in a bad condition
                currForce *= MathHelper.Lerp(0.5f, 2.0f, item.Condition / item.MaxCondition);

                item.Submarine.ApplyForce(currForce);

                UpdatePropellerDamage(deltaTime);

                if (item.AiTarget != null)
                {
                    var aiTarget = item.AiTarget;
                    aiTarget.SoundRange = MathHelper.Lerp(aiTarget.MinSoundRange, aiTarget.MaxSoundRange, currForce.Length() / maxForce);
                }
                if (item.CurrentHull != null)
                {
                    var aiTarget = item.CurrentHull.AiTarget;
                    float noise = MathHelper.Lerp(aiTarget.MinSoundRange, aiTarget.MaxSoundRange, currForce.Length() / maxForce);
                    aiTarget.SoundRange = Math.Max(noise, aiTarget.SoundRange);
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

            voltage -= deltaTime;
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

                character.LastDamageSource = item;
                propellerDamage.DoDamage(null, character, propellerWorldPos, 1.0f, true);
            }
        }

        partial void UpdateAnimation(float deltaTime);
        
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            force = MathHelper.Lerp(force, 0.0f, 0.1f);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power, signalStrength);

            if (connection.Name == "set_force")
            {
                if (float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float tempForce))
                {
                    targetForce = MathHelper.Clamp(tempForce, -100.0f, 100.0f);
                }
            }  
        }
    }
}
