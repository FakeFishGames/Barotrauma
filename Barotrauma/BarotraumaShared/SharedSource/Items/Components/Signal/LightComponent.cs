using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using Barotrauma.Networking;
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
        private float pulseFrequency, pulseAmount;
        private float range;
        private float flicker, flickerSpeed;
        private bool castShadows;
        private bool drawBehindSubs;

        private double lastToggleSignalTime;

        private string prevColorSignal;

        public PhysicsBody ParentBody;

        private bool isOn;

        private Turret turret;

        [Serialize(100.0f, true, description: "The range of the emitted light. Higher values are more performance-intensive.", alwaysUseInstanceValues: true),
            Editable(MinValueFloat = 0.0f, MaxValueFloat = 2048.0f)]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 4096.0f);
#if CLIENT
                item.ResetCachedVisibleSize();
                if (Light != null) { Light.Range = range; }
#endif
            }
        }

        private float rotation;
        public float Rotation
        {
            get { return rotation; }
            set 
            { 
                rotation = value;
                SetLightSourceTransformProjSpecific();
            }
        }

        [Editable, Serialize(true, true, description: "Should structures cast shadows when light from this light source hits them. " +
            "Disabling shadows increases the performance of the game, and is recommended for lights with a short range.", alwaysUseInstanceValues: true)]
        public bool CastShadows
        {
            get { return castShadows; }
            set
            {
                castShadows = value;
#if CLIENT
                if (Light != null) Light.CastShadows = value;
#endif
            }
        }

        [Editable, Serialize(false, true, description: "Lights drawn behind submarines don't cast any shadows and are much faster to draw than shadow-casting lights. " +
            "It's recommended to enable this on decorative lights outside the submarine's hull.", alwaysUseInstanceValues: true)]
        public bool DrawBehindSubs
        {
            get { return drawBehindSubs; }
            set
            {
                drawBehindSubs = value;
#if CLIENT
                if (Light != null) Light.IsBackground = drawBehindSubs;
#endif
            }
        }

        [Editable, Serialize(false, true, description: "Is the light currently on.", alwaysUseInstanceValues: true)]
        public bool IsOn
        {
            get { return isOn; }
            set
            {
                if (isOn == value && IsActive == value) { return; }

                IsActive = isOn = value;
                SetLightSourceState(value, value ? lightBrightness : 0.0f);
                OnStateChanged();
            }
        }

        [Editable, Serialize(0.0f, false, description: "How heavily the light flickers. 0 = no flickering, 1 = the light will alternate between completely dark and full brightness.")]
        public float Flicker
        {
            get { return flicker; }
            set
            {
                flicker = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                if (Light != null) { Light.LightSourceParams.Flicker = flicker; }
#endif
            }
        }

        [Editable, Serialize(1.0f, false, description: "How fast the light flickers.")]
        public float FlickerSpeed
        {
            get { return flickerSpeed; }
            set
            {
                flickerSpeed = value;
#if CLIENT
                if (Light != null) { Light.LightSourceParams.FlickerSpeed = flickerSpeed; }
#endif
            }
        }

        [Editable, Serialize(0.0f, true, description: "How rapidly the light pulsates (in Hz). 0 = no blinking.")]
        public float PulseFrequency
        {
            get { return pulseFrequency; }
            set
            {
                pulseFrequency = MathHelper.Clamp(value, 0.0f, 60.0f);
#if CLIENT
                if (Light != null) { Light.LightSourceParams.PulseFrequency = pulseFrequency; }
#endif
            }
        }

        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, DecimalCount = 2), Serialize(0.0f, true, description: "How much light pulsates (in Hz). 0 = not at all, 1 = alternates between full brightness and off.")]
        public float PulseAmount
        {
            get { return pulseAmount; }
            set
            {
                pulseAmount = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                if (Light != null) { Light.LightSourceParams.PulseAmount = pulseAmount; }
#endif
            }
        }

        [Editable, Serialize(0.0f, true, description: "How rapidly the light blinks on and off (in Hz). 0 = no blinking.")]
        public float BlinkFrequency
        {
            get { return blinkFrequency; }
            set
            {
                blinkFrequency = MathHelper.Clamp(value, 0.0f, 60.0f);
#if CLIENT
                if (Light != null) { Light.LightSourceParams.BlinkFrequency = blinkFrequency; }
#endif
            }
        }

        [InGameEditable(FallBackTextTag = "connection.setcolor"), Serialize("255,255,255,255", true, description: "The color of the emitted light (R,G,B,A).", alwaysUseInstanceValues: true)]
        public Color LightColor
        {
            get { return lightColor; }
            set
            {
                lightColor = value;
#if CLIENT
                if (Light != null)
                {
                    Light.Color = IsActive ? lightColor : Color.Transparent;
                }
#endif
            }
        }

        [Serialize(false, false, description: "If enabled, the component will ignore continuous signals received in the toggle input (i.e. a continuous signal will only toggle it once).")]
        public bool IgnoreContinuousToggle
        {
            get;
            set;
        }

        public override void Move(Vector2 amount)
        {
#if CLIENT
            Light.Position += amount;
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
                if (base.IsActive == value) { return; }
                base.IsActive = isOn = value;
                SetLightSourceState(value, value ? lightBrightness : 0.0f);                
            }
        }

        public LightComponent(Item item, XElement element)
            : base(item, element)
        {
#if CLIENT
            Light = new LightSource(element)
            {
                ParentSub = item.CurrentHull?.Submarine,
                Position = item.Position,
                CastShadows = castShadows,                
                IsBackground = drawBehindSubs,
                SpriteScale = Vector2.One * item.Scale,
                Range = range
            };
            Light.LightSourceParams.Flicker = flicker;
            Light.LightSourceParams.FlickerSpeed = FlickerSpeed;
            Light.LightSourceParams.PulseAmount = pulseAmount;
            Light.LightSourceParams.PulseFrequency = pulseFrequency;
            Light.LightSourceParams.BlinkFrequency = blinkFrequency;
#endif

            IsActive = IsOn;
            item.AddTag("light");
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            SetLightSourceState(IsActive, lightBrightness);
            turret = item.GetComponent<Turret>();
        }

        public override void OnMapLoaded()
        {
            if (item.body == null && powerConsumption <= 0.0f && Parent == null && turret == null && IsOn &&
                (statusEffectLists == null || !statusEffectLists.ContainsKey(ActionType.OnActive)) && 
                (IsActiveConditionals == null || IsActiveConditionals.Count == 0))
            {
                lightBrightness = 1.0f;
                SetLightSourceState(true, lightBrightness);
                SetLightSourceTransformProjSpecific();
                base.IsActive = false;
                isOn = true;
#if CLIENT
                Light.ParentSub = item.Submarine;
#endif
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.AiTarget != null)
            {
                UpdateAITarget(item.AiTarget);
            }
            UpdateOnActiveEffects(deltaTime);

            if (powerIn == null && powerConsumption > 0.0f) { Voltage -= deltaTime; }

#if CLIENT
            Light.ParentSub = item.Submarine;
#endif
            if (item.Container != null)
            {
                SetLightSourceState(false, 0.0f);
                return;
            }

            SetLightSourceTransformProjSpecific();

            PhysicsBody body = ParentBody ?? item.body;
            if (body != null && !body.Enabled)
            {
                SetLightSourceState(false, 0.0f);
                return;                
            }

            currPowerConsumption = powerConsumption;
            if (Rand.Range(0.0f, 1.0f) < 0.05f && Voltage < Rand.Range(0.0f, MinVoltage))
            {
#if CLIENT
                if (Voltage > 0.1f)
                {
                    SoundPlayer.PlaySound("zap", item.WorldPosition, hullGuess: item.CurrentHull);
                }
#endif
                lightBrightness = 0.0f;
            }
            else
            {
                lightBrightness = MathHelper.Lerp(lightBrightness, powerConsumption <= 0.0f ? 1.0f : Math.Min(Voltage, 1.0f), 0.1f);
            }

            SetLightSourceState(true, lightBrightness);
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            SetLightSourceState(false, 0.0f);
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return true;
        }

        partial void OnStateChanged();

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "toggle":
                    if (signal.value != "0")
                    {
                        if (!IgnoreContinuousToggle || lastToggleSignalTime < Timing.TotalTime - 0.1)
                        {
                            IsOn = !IsOn;
                        }
                        lastToggleSignalTime = Timing.TotalTime;
                    }
                    break;
                case "set_state":
                    IsOn = signal.value != "0";
                    break;
                case "set_color":
                    if (signal.value != prevColorSignal)
                    {
                        LightColor = XMLExtensions.ParseColor(signal.value, false);
#if CLIENT
                        SetLightSourceState(Light.Enabled, currentBrightness);
#endif
                        prevColorSignal = signal.value;
                    }
                    break;
            }
        }

        private void UpdateAITarget(AITarget target)
        {
            if (!IsActive) { return; }
            if (target.MaxSightRange <= 0)
            {
                target.MaxSightRange = Range * 5;
            }
            target.SightRange = Math.Max(target.SightRange, target.MaxSightRange * lightBrightness);
        }

        partial void SetLightSourceState(bool enabled, float brightness);

        public void SetLightSourceTransform()
        {
            SetLightSourceTransformProjSpecific();
        }

        partial void SetLightSourceTransformProjSpecific();
    }
}
