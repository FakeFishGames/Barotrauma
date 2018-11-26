using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using Barotrauma.Networking;
using Lidgren.Network;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Lights;
#endif

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private Color lightColor;
        private float lightBrightness;
        private float blinkFrequency;
        private float range;
        private float flicker;
        private bool castShadows;

        private float blinkTimer;
        
        public PhysicsBody ParentBody;

        [Editable(0.0f, 2048.0f), Serialize(100.0f, true)]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 2048.0f);
#if CLIENT
                if (light != null) light.Range = range;
#endif
            }
        }

        public float Rotation;

        [Editable(ToolTip = "Should structures cast shadows when light from this light source hits them. "+
            "Disabling shadows increases the performance of the game, and is recommended for lights with a short range."), Serialize(true, true)]
        public bool CastShadows
        {
            get { return castShadows; }
            set
            {
                castShadows = value;
#if CLIENT
                if (light != null) light.CastShadows = value;
#endif
            }
        }

        [Editable, Serialize(false, true)]
        public bool IsOn
        {
            get { return IsActive; }
            set
            {
                if (IsActive == value) return;
                
                IsActive = value;
#if SERVER
                if (GameMain.Server != null) item.CreateServerEvent(this);
#endif
            }
        }
        
        [Serialize(0.0f, false)]
        public float Flicker
        {
            get { return flicker; }
            set
            {
                flicker = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Editable, Serialize(0.0f, true)]
        public float BlinkFrequency
        {
            get { return blinkFrequency; }
            set
            {
                blinkFrequency = MathHelper.Clamp(value, 0.0f, 60.0f);
            }
        }

        [InGameEditable, Serialize("1.0,1.0,1.0,1.0", true)]
        public Color LightColor
        {
            get { return lightColor; }
            set
            {
                lightColor = value;
#if CLIENT
                if (light != null) light.Color = lightColor;
#endif
            }
        }

        public override void Move(Vector2 amount)
        {
#if CLIENT
            light.Position += amount;
#endif
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
#if CLIENT
                if (light == null) return;
                light.Color = value ? lightColor : Color.Transparent;
                if (!value) lightBrightness = 0.0f;
#endif
            }
        }

        public LightComponent(Item item, XElement element)
            : base (item, element)
        {
#if CLIENT
            light = new LightSource(element);
            light.ParentSub = item.CurrentHull?.Submarine;
            light.Position = item.Position;
            light.CastShadows = castShadows;
#endif

            IsActive = IsOn;
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);
            if (AITarget != null) AITarget.Enabled = voltage > minVoltage || powerConsumption <= 0.0f;

#if CLIENT
            light.ParentSub = item.Submarine;
            if (item.Container != null)
            {
                light.Color = Color.Transparent;
                return;
            }
            light.Position = ParentBody != null ? ParentBody.Position : item.Position;
#endif

            PhysicsBody body = ParentBody ?? item.body;

            if (body != null)
            {
#if CLIENT
                light.Rotation = body.Dir > 0.0f ? body.Rotation : body.Rotation - MathHelper.Pi;
                light.LightSpriteEffect = (body.Dir > 0.0f) ? SpriteEffects.None : SpriteEffects.FlipVertically;
#endif
                if (!body.Enabled)
                {
#if CLIENT
                    light.Color = Color.Transparent;
#endif
                    return;
                }
            }
            else
            {
#if CLIENT
                light.Rotation = -Rotation;
#endif
            }
            
            if (powerConsumption == 0.0f)
            {
                voltage = 1.0f;
            }
            else
            {
                currPowerConsumption = powerConsumption;                
            }

            if (Rand.Range(0.0f, 1.0f) < 0.05f && voltage < Rand.Range(0.0f, minVoltage))
            {
#if CLIENT
                if (voltage > 0.1f && sparkSounds.Count > 0) 
                {
                    var sparkSound = sparkSounds[Rand.Int(sparkSounds.Count)];
                    SoundPlayer.PlaySound(sparkSound.Sound, sparkSound.Volume, sparkSound.Range, item.WorldPosition, item.CurrentHull);
                }
#endif
                lightBrightness = 0.0f;
            }
            else
            {
                lightBrightness = MathHelper.Lerp(lightBrightness, Math.Min(voltage, 1.0f), 0.1f);
            }

            if (blinkFrequency > 0.0f)
            {
                blinkTimer = (blinkTimer + deltaTime * blinkFrequency) % 1.0f;                
            }

            if (blinkTimer > 0.5f)
            {
#if CLIENT
                light.Color = Color.Transparent;
#endif
            }
            else
            {
#if CLIENT
                light.Color = lightColor * lightBrightness * (1.0f - Rand.Range(0.0f, Flicker));
                light.Range = range;
#endif
                item.SightRange = Math.Max(range * (float)Math.Sqrt(lightBrightness), item.SightRange);
            }
            
            voltage = 0.0f;
        }
                
#if CLIENT
        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            light.Color = Color.Transparent;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            light.Remove();
        }
#endif
        public override bool Use(float deltaTime, Character character = null)
        {
            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power, signalStrength);

            switch (connection.Name)
            {
                case "toggle":
                    IsActive = !IsActive;
                    break;
                case "set_state":           
                    IsActive = (signal != "0");                   
                    break;
                case "set_color":
                    LightColor = XMLExtensions.ParseColor(signal, false);
                    break;
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsOn);
        }
    }
}
