using Microsoft.Xna.Framework;
using Barotrauma.Lights;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class LightComponent : Powered, IDrawableComponent
    {

        private Color lightColor;

        private LightSource light;

        private float range;

        private float lightBrightness;

        private float flicker;

        private bool castShadows;

        [Editable, HasDefaultValue(100.0f, true)]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 2048.0f);
            }
        }

        [Editable, HasDefaultValue(true, true)]
        public bool CastShadows
        {
            get { return castShadows; }
            set
            {
                castShadows = value;
                if (light != null) light.CastShadows = value;
            }
        }

        [Editable, HasDefaultValue(false, true)]
        public bool IsOn
        {
            get { return IsActive; }
            set
            {
                IsActive = value;
            }
        }
        
        [HasDefaultValue(0.0f, false)]
        public float Flicker
        {
            get { return flicker; }
            set
            {
                flicker = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [InGameEditable, HasDefaultValue("1.0,1.0,1.0,1.0", true)]
        public string LightColor
        {
            get { return ToolBox.Vector4ToString(lightColor.ToVector4(), "0.00"); }
            set
            {
                Vector4 newColor = ToolBox.ParseToVector4(value, false);
                newColor.X = MathHelper.Clamp(newColor.X, 0.0f, 1.0f);
                newColor.Y = MathHelper.Clamp(newColor.Y, 0.0f, 1.0f);
                newColor.Z = MathHelper.Clamp(newColor.Z, 0.0f, 1.0f);
                newColor.W = MathHelper.Clamp(newColor.W, 0.0f, 1.0f);
                lightColor = new Color(newColor);
            }
        }

        public override void Move(Vector2 amount)
        {
            light.Position += amount;
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

                if (light == null) return;
                light.Color = value ? lightColor : Color.Transparent;
                if (!value) lightBrightness = 0.0f;
            }
        }

        public LightComponent(Item item, XElement element)
            : base (item, element)
        {
            light = new LightSource(element);
            light.Submarine = item.CurrentHull == null ? null : item.CurrentHull.Submarine;
            light.Position = item.Position;
            light.CastShadows = castShadows;

            IsActive = IsOn;

            //foreach (XElement subElement in element.Elements())
            //{
            //    if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;

            //    light.LightSprite = new Sprite(subElement);
            //    break;
            //}
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
           
            light.Submarine = item.Submarine;
            
            if (item.Container != null)
            {
                light.Color = Color.Transparent;
                return;
            }

            if (item.body != null)
            {
                light.Position = item.Position;
                light.Rotation = item.body.Dir > 0.0f ? item.body.Rotation : item.body.Rotation - MathHelper.Pi;

                if (!item.body.Enabled)
                {
                    light.Color = Color.Transparent;
                    return;
                }
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
                if (voltage > 0.1f) sparkSounds[Rand.Int(sparkSounds.Length)].Play(1.0f, 400.0f, item.WorldPosition);
                lightBrightness = 0.0f;
            }
            else
            {
                lightBrightness = MathHelper.Lerp(lightBrightness, Math.Min(voltage, 1.0f), 0.1f);

                ApplyStatusEffects(ActionType.OnActive, deltaTime);
            }

            light.Color = lightColor * lightBrightness * (1.0f-Rand.Range(0.0f,Flicker));
            light.Range = range * (float)Math.Sqrt(lightBrightness);

            voltage = 0.0f;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return true;
        }

        public void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, bool editing = false)
        {
            if (light.LightSprite != null && (item.body == null || item.body.Enabled))
            {
                light.LightSprite.Draw(spriteBatch, new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), lightColor * lightBrightness, 0.0f, 1.0f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, item.Sprite.Depth - 0.0001f);
            } 
        }
        
        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            light.Remove();
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, sender, power);

            switch (connection.Name)
            {
                case "toggle":
                    IsActive = !IsActive;
                    break;
                case "set_state":           
                    IsActive = (signal != "0");                   
                    break;
                case "set_color":
                    LightColor = signal;
                    break;
            }
        }
    }
}
