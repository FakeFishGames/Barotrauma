﻿using Microsoft.Xna.Framework;
using System;
using Barotrauma.Networking;
using Barotrauma.Extensions;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Lights;
#endif

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private Color lightColor;
        /// <summary>
        /// The current brightness of the light source, affected by powerconsumption/voltage
        /// </summary>
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

        [Serialize(100.0f, IsPropertySaveable.Yes, description: "The range of the emitted light. Higher values are more performance-intensive.", alwaysUseInstanceValues: true),
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

        [Editable, Serialize(true, IsPropertySaveable.Yes, description: "Should structures cast shadows when light from this light source hits them. " +
            "Disabling shadows increases the performance of the game, and is recommended for lights with a short range. Lights that are set to be drawn behind subs don't cast shadows, regardless of this setting.", alwaysUseInstanceValues: true)]
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

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Lights drawn behind submarines don't cast any shadows and are much faster to draw than shadow-casting lights. " +
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

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Is the light currently on.", alwaysUseInstanceValues: true)]
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

        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "How heavily the light flickers. 0 = no flickering, 1 = the light will alternate between completely dark and full brightness.")]
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

        [Editable, Serialize(1.0f, IsPropertySaveable.No, description: "How fast the light flickers.")]
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

        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "How rapidly the light pulsates (in Hz). 0 = no blinking.")]
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

        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, DecimalCount = 2), Serialize(0.0f, IsPropertySaveable.Yes, description: "How much light pulsates (in Hz). 0 = not at all, 1 = alternates between full brightness and off.")]
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

        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "How rapidly the light blinks on and off (in Hz). 0 = no blinking.")]
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

        [InGameEditable(FallBackTextTag = "connection.setcolor"), Serialize("255,255,255,255", IsPropertySaveable.Yes, description: "The color of the emitted light (R,G,B,A).", alwaysUseInstanceValues: true)]
        public Color LightColor
        {
            get { return lightColor; }
            set
            {
                lightColor = value;
                //reset previously received signal to force updating the color if we receive a set_color signal after the color has been modified manually
                prevColorSignal = string.Empty;
#if CLIENT
                if (Light != null)
                {
                    Light.Color = IsOn ? lightColor.Multiply(lightColorMultiplier) : Color.Transparent;
                }
#endif
            }
        }

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, the component will ignore continuous signals received in the toggle input (i.e. a continuous signal will only toggle it once).")]
        public bool IgnoreContinuousToggle
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Should the light sprite be drawn on the item using alpha blending, in addition to being rendered in the light map? Can be used to make the light sprite stand out more.")]
        public bool AlphaBlend
        {
            get;
            set;
        }

        public float TemporaryFlickerTimer;

        public override void Move(Vector2 amount, bool ignoreContacts = false)
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

        public LightComponent(Item item, ContentXElement element)
            : base(item, element)
        {
#if CLIENT
            Light = new LightSource(element)
            {
                ParentSub = item.CurrentHull?.Submarine,
                Position = item.Position,
                CastShadows = castShadows,                
                IsBackground = drawBehindSubs,
                SpriteScale = Vector2.One * item.Scale * LightSpriteScale,
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
            if (item.body != null)
            {
                item.body.FarseerBody.OnEnabled += CheckIfNeedsUpdate;
                item.body.FarseerBody.OnDisabled += CheckIfNeedsUpdate;
            }
#if CLIENT
            Drawable = AlphaBlend && Light.LightSprite != null;
            if (Screen.Selected.IsEditor)
            {
                OnMapLoaded();
            }
#endif
        }

        public override void OnMapLoaded()
        {
#if CLIENT
            if (item.HiddenInGame)
            {
                Light.Enabled = false;
            }
#endif
            CheckIfNeedsUpdate();
        }

        public void CheckIfNeedsUpdate()
        {
            if (!IsOn) 
            {
                base.IsActive = false;
                return; 
            }

            if ((item.body == null || !item.body.Enabled) && 
                powerConsumption <= 0.0f && Parent == null && turret == null &&
                (statusEffectLists == null || !statusEffectLists.ContainsKey(ActionType.OnActive)) &&
                (IsActiveConditionals == null || IsActiveConditionals.Count == 0))
            {
                if (item.body != null && !item.body.Enabled)
                {
                    lightBrightness = 0.0f;
                    SetLightSourceState(false, 0.0f);
                }
                else
                {
                    lightBrightness = 1.0f;
                    SetLightSourceState(true, lightBrightness);
                }
                isOn = true;
                SetLightSourceTransformProjSpecific();
                base.IsActive = false;
#if CLIENT
                Light.ParentSub = item.Submarine;
#endif
            }
            else
            {
                base.IsActive = true;
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.AiTarget != null)
            {
                UpdateAITarget(item.AiTarget);
            }
            UpdateOnActiveEffects(deltaTime);
            //something in UpdateOnActiveEffects may deactivate the light -> return so we don't turn it back on
            if (!IsActive) { return; }

#if CLIENT
            Light.ParentSub = item.Submarine;
#endif
            var ownerCharacter = item.GetRootInventoryOwner() as Character;
            if ((item.Container != null && ownerCharacter == null) || 
                (ownerCharacter != null && ownerCharacter.InvisibleTimer > 0.0f))
            {
                lightBrightness = 0.0f;
                SetLightSourceState(false, 0.0f);
                return;
            }
            SetLightSourceTransformProjSpecific();

            PhysicsBody body = ParentBody ?? item.body;
            if (body != null && !body.Enabled)
            {
                lightBrightness = 0.0f;
                SetLightSourceState(false, 0.0f);
                return;
            }

            TemporaryFlickerTimer -= deltaTime;

            //currPowerConsumption = powerConsumption;
            if (Rand.Range(0.0f, 1.0f) < 0.05f && (Voltage < Rand.Range(0.0f, MinVoltage) || TemporaryFlickerTimer > 0.0f))
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
                        SetLightSourceState(Light.Enabled, lightColorMultiplier);
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
