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
        
        private readonly Attack propellerDamage;

        private float damageTimer;

        private bool hasPower;

        private float prevVoltage;

        private float controlLockTimer;

        public Character User;

        [Editable(0.0f, 10000000.0f), 
        Serialize(500.0f, true, description: "The amount of force exerted on the submarine when the engine is operating at full power.")]
        public float MaxForce
        {
            get { return maxForce; }
            set
            {
                maxForce = Math.Max(0.0f, value);
            }
        }

        [Editable, Serialize("0.0,0.0", true, 
            description: "The position of the propeller as an offset from the item's center (in pixels)."+
            " Determines where the particles spawn and the position that causes characters to take damage from the engine if the PropellerDamage is defined.")]
        public Vector2 PropellerPos
        {
            get;
            set;
        }

        [Editable, Serialize(false, true)]
        public bool DisablePropellerDamage
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
            get { return Math.Abs((force / 100.0f) * (MinVoltage <= 0.0f ? 1.0f : Math.Min(prevVoltage / MinVoltage, 1.0f))); }
        }

        public float CurrentBrokenVolume
        {
            get 
            {
                if (item.ConditionPercentage > 10.0f) { return 0.0f; }
                return Math.Abs(targetForce / 100.0f) * (1.0f - item.ConditionPercentage / 10.0f); 
            }
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

            controlLockTimer -= deltaTime;

            currPowerConsumption = Math.Abs(targetForce) / 100.0f * powerConsumption;
            //engines consume more power when in a bad condition
            item.GetComponent<Repairable>()?.AdjustPowerConsumption(ref currPowerConsumption);

            if (powerConsumption == 0.0f) { Voltage = 1.0f; }

            prevVoltage = Voltage;
            hasPower = Voltage > MinVoltage;

            Force = MathHelper.Lerp(force, (Voltage < MinVoltage) ? 0.0f : targetForce, 0.1f);
            if (Math.Abs(Force) > 1.0f)
            {
                //arbitrary multiplier that was added to changes in submarine mass without having to readjust all engines
                float forceMultiplier = 0.1f;
                if (User != null)
                {
                    forceMultiplier *= MathHelper.Lerp(0.5f, 2.0f, (float)Math.Sqrt(User.GetSkillLevel("helm") / 100));
                }

                float voltageFactor = MinVoltage <= 0.0f ? 1.0f : Math.Min(Voltage, 1.0f);
                Vector2 currForce = new Vector2(force * maxForce * forceMultiplier * voltageFactor, 0.0f);
                //less effective when in a bad condition
                currForce *= MathHelper.Lerp(0.5f, 2.0f, item.Condition / item.MaxCondition);
                if (item.Submarine.FlippedX) { currForce *= -1; }
                item.Submarine.ApplyForce(currForce);
                UpdatePropellerDamage(deltaTime);
                float maxChangeSpeed = 0.5f;
                float modifier = 2;
                float noise = MathUtils.NearlyEqual(0.0f, maxForce) ? 0.0f : currForce.Length() * forceMultiplier * modifier / maxForce;
                float min = Math.Max(1 - maxChangeSpeed, 0);
                float max = 1 + maxChangeSpeed;
                UpdateAITargets(Math.Clamp(noise, min, max), deltaTime);
#if CLIENT
                particleTimer -= deltaTime;
                if (particleTimer <= 0.0f)
                {
                    Vector2 particleVel = -currForce.ClampLength(5000.0f) / 5.0f;
                    GameMain.ParticleManager.CreateParticle("bubbles", item.WorldPosition + PropellerPos * item.Scale,
                        particleVel * Rand.Range(0.9f, 1.1f),
                        0.0f, item.CurrentHull);
                    particleTimer = 1.0f / particlesPerSec;
                }                
#endif
            }
        }

        private void UpdateAITargets(float increaseSpeed, float deltaTime)
        {
            if (item.AiTarget != null)
            {
                item.AiTarget.IncreaseSoundRange(deltaTime, increaseSpeed);
                if (item.CurrentHull != null && item.CurrentHull.AiTarget != null)
                {
                    // It's possible that some othe item increases the hull's soundrange more than the engine.
                    item.CurrentHull.AiTarget.SoundRange = Math.Max(item.CurrentHull.AiTarget.SoundRange, item.AiTarget.SoundRange);
                }
            }
        }

        private void UpdatePropellerDamage(float deltaTime)
        {
            if (DisablePropellerDamage) { return; }

            damageTimer += deltaTime;
            if (damageTimer < 0.5f) { return; }
            damageTimer = 0.1f;

            if (propellerDamage == null) { return; }

            float scaledDamageRange = propellerDamage.DamageRange * item.Scale;

            Vector2 propellerWorldPos = item.WorldPosition + PropellerPos * item.Scale;
            foreach (Character character in Character.CharacterList)
            {
                if (character.Submarine != null || !character.Enabled || character.Removed) { continue; }
                float distSqr = Vector2.DistanceSquared(character.WorldPosition, propellerWorldPos);
                if (distSqr > scaledDamageRange * scaledDamageRange) { continue; }
                character.LastDamageSource = item;
                propellerDamage.DoDamage(null, character, propellerWorldPos, 1.0f, true);
            }
        }

        partial void UpdateAnimation(float deltaTime);
        
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            force = MathHelper.Lerp(force, 0.0f, 0.1f);
        }

        public override void FlipX(bool relativeToSub)
        {
            PropellerPos = new Vector2(-PropellerPos.X, PropellerPos.Y);
        }

        public override void FlipY(bool relativeToSub)
        {
            PropellerPos = new Vector2(PropellerPos.X, -PropellerPos.Y);
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            base.ReceiveSignal(signal, connection);

            if (connection.Name == "set_force")
            {
                if (float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float tempForce))
                {
                    controlLockTimer = 0.1f;
                    targetForce = MathHelper.Clamp(tempForce, -100.0f, 100.0f);
                    User = signal.sender;
                }
            }  
        }

        public override XElement Save(XElement parentElement)
        {
            Vector2 prevPropellerPos = PropellerPos;
            //undo flipping before saving
            if (item.FlippedX) { PropellerPos = new Vector2(-PropellerPos.X, PropellerPos.Y); }
            if (item.FlippedY) { PropellerPos = new Vector2(PropellerPos.X, -PropellerPos.Y); }
            XElement element = base.Save(parentElement);
            PropellerPos = prevPropellerPos;
            return element;
        }
    }
}
