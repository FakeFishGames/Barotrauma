using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics.Dynamics;
using System.Collections.Immutable;

namespace Barotrauma.Items.Components
{
    public partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite barrelSprite, railSprite;
        private Sprite barrelSpriteBroken, railSpriteBroken;
        private readonly List<ChargeSprite> chargeSprites = [];
        private readonly List<Sprite> spinningBarrelSprites = [];
        
        private readonly record struct ChargeSprite(Sprite Sprite, Vector2 ChargeOffset, float ChargeRotation);

        /// <summary>
        /// Sentinel value that represents the turret being launched without a projectile in network events
        /// </summary>
        const ushort LaunchWithoutProjectileId = ushort.MaxValue;

        private Vector2 barrelPos;
        private Vector2 transformedBarrelPos;
        
        private float targetRotation;

        private float reload;
        /// <summary>
        /// The reload time applied by the most recent launch (including the delay between bursts and reload modifiers).
        /// </summary>
        private float lastLaunchReload;
        private int shotCounter;

        private float minRotation, maxRotation;

        private Camera cam;

        private float angularVelocity;

        private int failedLaunchAttempts;

        private float currentChargeTime;
        private bool tryingToCharge;

        private const float LineOfSightCheckInterval = 0.5f;
        private (Body WorldTarget, Body TransformedTarget, double Time) lastLineOfSightCheck;
        private (Character Target, bool CanSee, double Time) lastCanSeeTargetCheck;

        private enum ChargingState
        {
            Inactive,
            WindingUp,
            WindingDown,
        }

        private ChargingState currentChargingState;

        private readonly List<Projectile> activeProjectiles = new List<Projectile>();
        public IEnumerable<Projectile> ActiveProjectiles => activeProjectiles;

        private Character _user;
        public Character User
        {
            get => _user;
            set
            {
                _user = value;
                if (_user != null)
                {
                    resetActiveUserTimer = ResetActiveUseAfter;
                    resetUserTimer = ResetUserAfter;
                    hasBeenActivelyUsed = true;   
                }
            }
        }
        
        private float resetUserTimer;
        private float resetActiveUserTimer;
        private const float ResetUserAfter = 10f;
        private const float ResetActiveUseAfter = 1f;
        
        private bool hasBeenActivelyUsed;
        public bool IsBeingActivelyUsed => User != null && hasBeenActivelyUsed;
        public bool IsReloading => reload > 0.0f;

        private float aiFindTargetTimer;
        private ISpatialEntity currentTarget;
        private const float CrewAiFindTargetMaxInterval = 1.0f;
        private const float CrewAIFindTargetMinInverval = 0.2f;

        /// <summary>
        /// Bots consider the projectile to move at least this fast when calculating how far ahead a moving target they need to aim.
        /// Aiming ahead doesn't work reliably with very slow projectiles, because we'd need to take into account drag and gravity, 
        /// and the target would most likely move in a different direction anyway before the projectile reaches it.
        /// </summary>
        private const float MinimumProjectileVelocityForAimAhead = 20.0f;

        /// <summary>
        /// Bots don't try to aim ahead a moving target by more than this amount. If the target is very fast and/or the projectile very slow,
        /// we'd need to aim so far ahead it'd most likely fail anyway.
        /// </summary>
        private const float MaximumAimAhead = 10.0f;

        private float projectileSpeed;
        private Item previousAmmo;

        private int currentLoaderIndex;

        private const float TinkeringPowerCostReduction = 0.2f;
        private const float TinkeringDamageIncrease = 0.2f;
        private const float TinkeringReloadDecrease = 0.2f;

        private List<LightComponent> lightComponents;

        private Projectile lastProjectile;

        private readonly bool isSlowTurret;

        private float barrelRotation;
        public float BarrelRotation 
        {
            get => barrelRotation;
            private set
            {
                barrelRotation = value;
#if CLIENT
                railSpriteRotation = BarrelRotation + MathHelper.PiOver2;
#endif
            }
        }

        public float FixedFiringRotation => item.RotationRad - MathHelper.PiOver2;

        public float FiringRotation => useFixedFiringPos ? FixedFiringRotation : BarrelRotation;
        
        private Vector2 transformedFixedFiringPos;
        private Vector2 fixedFiringPos;

        private readonly bool useFixedFiringPos;
        [Serialize("0,0", IsPropertySaveable.No, description: "The projectile launching position relative to the upper left corner of the base sprite (in pixels). If set, the barrel is ignored when shooting.")]
        public Vector2 FixedFiringPos
        {
            get
            {
                return fixedFiringPos;
            }
            set
            {
                fixedFiringPos = value;
                UpdateTransformedBarrelPos();
            }
        }

        public Vector2 TransformedBarrelPos => transformedBarrelPos;

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplies the damage the turret deals by this amount.")]
        public float DamageMultiplier { get; set; }

        [Serialize(1, IsPropertySaveable.No, description: "How many projectiles the weapon launches when fired once.")]
        public int ProjectileCount { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the turret be fired without projectiles (causing it just to execute the OnUse effects and the firing animation without actually firing anything).")]
        public bool LaunchWithoutProjectile { get; set; }
        
        [Serialize(true, IsPropertySaveable.No, description: 
            "If set to false, the turret will not launch if the RequiredItems defined in the linked loaders aren't present."+
            " Can be used in conjunction with LaunchWithoutProjectile to create turrets that don't launch any actual projectiles (but e.g. use a RepairTool to fire some kind of a continuous beam),"+
            " but still require there to be ammo in a linked loader.")]
        public bool LaunchWithoutRequiredItemsInLoaders { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "How fast the turret can rotate while firing (for charged weapons).")]
        public float FiringRotationSpeedModifier { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Whether the turret should always charge-up fully to shoot.")]
        public bool SingleChargedShot { get; set; }

        private float prevScale;
        float prevBaseRotation;
        [Serialize(0.0f, IsPropertySaveable.Yes, description: "The angle of the turret's base in degrees.", alwaysUseInstanceValues: true)]
        public float BaseRotation
        {
            get { return item.Rotation; }
            set
            {
                item.Rotation = value;
                UpdateTransformedBarrelPos();
            }
        }

        [Serialize(3500.0f, IsPropertySaveable.No, description: "How close to a target the turret has to be for an AI character to fire it.")]
        public float AIRange { get; set; }

        private float _maxAngleOffset;
        [Serialize(10.0f, IsPropertySaveable.No, description: "How much off the turret can be from the target for the AI to shoot. In degrees.")]
        public float MaxAngleOffset
        {
            get => _maxAngleOffset;
            private set => _maxAngleOffset = MathHelper.Clamp(value, 0f, 180f);
        }

        [Serialize(1.1f, IsPropertySaveable.No, description: "How much does the AI prefer currently selected targets over new targets closer to the turret.")]
        public float AICurrentTargetPriorityMultiplier { get; private set; }

        [Serialize(false, IsPropertySaveable.No, description: "Should AI character keep aiming with the turret, regardless if it's out of power or ammo, if there's currently projectiles active? Useful for e.g. homing ammo that's aimed with the turret.")]
        public bool AIKeepAimingWhenProjectilesActive { get; private set; }

        [Serialize(-1.0f, IsPropertySaveable.No,
            "If set, bots must always be aiming at least distance away from the turret, and cannot fire if the target is closer than this." +
            "Can be used to prevent bots from accidentally hitting the sub with e.g. guided torpedoes or weapons that do AOE damage" +
            $"when a target gets very close to the sub. -1 == no limit.")]
        public float AIMinAimDistance { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Bots won't fire more projectile if there's this many projectiles still active. If set to -1, there is no limit.")]
        public int AIMaxActiveProjectiles { get; set; }

        [Serialize(-1, IsPropertySaveable.No, description: "The turret won't fire additional projectiles if the number of previously fired, still active projectiles reaches this limit. If set to -1, there is no limit to the number of projectiles.")]
        public int MaxActiveProjectiles { get; set; }

        /// <summary>
        /// <see cref="Timing.TotalTimeUnpaused"/> when the turret was fired the last time.
        /// </summary>
        public float LastFiredTime { get; set; }

        /// <summary>
        /// Has the <see cref="MaxActiveProjectiles">maximum number of active projectiles</see> been reached?
        /// </summary>
        public bool MaxActiveProjectilesReached => MaxActiveProjectiles >= 0 && activeProjectiles.Count >= MaxActiveProjectiles;

        /// <summary>
        /// Has the <see cref="AIMaxActiveProjectiles">maximum number of active projectiles bots can fire</see> been reached?
        /// </summary>
        public bool AIMaxActiveProjectilesReached => MaxActiveProjectilesReached || (AIMaxActiveProjectiles >= 0 && activeProjectiles.Count >= AIMaxActiveProjectiles);

        public override float OffsetOnSelectedMultiplier => 
            (GetLoadedProjectiles().FirstOrDefault() ?? lastProjectile)?.Item.OffsetOnSelectedMultiplier ?? 1.0f;

        #region Editable properties

        [Serialize(5.0f, IsPropertySaveable.No, description: "The period of time the user has to wait between shots."), 
         Editable(0.0f, 1000.0f, decimals: 3)]
        public float Reload { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: 
            "Reload time that triggers when the turret runs out of power."+
            " Can be used to e.g. put continuously firing turrets on a cooldown ones the power runs out, to prevent them from \"stuttering\" as the supercapacitors charge while the turret is firing.")]
        public float ReloadWhenOutOfPower { get; set; }

        [Serialize(1, IsPropertySaveable.No, description: "How many projectiles needs to be shot before we add an extra break? Think of the double coilgun."), 
         Editable(1, 100)]
        public int ShotsPerBurst { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "An extra delay between the bursts. Added to the reload."),
         Editable(0.0f, 1000.0f, decimals: 3)]
        public float DelayBetweenBursts { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Modifies the duration of retraction of the barrel after recoil to get back to the original position after shooting. Reload time affects this too."),
         Editable(0.0f, 10f, DecimalCount = 2, ValueStep = 0.1f)]
        public float RetractionDurationMultiplier { get; set; }

        [Serialize(0.1f, IsPropertySaveable.No, description: "How quickly the recoil moves the barrel after launching."), 
         Editable(0f, 10f, DecimalCount = 2, ValueStep = 0.1f)]
        public float RecoilTime { get; set; }

        [Serialize(0f, IsPropertySaveable.No, description: "How long the barrel stays in place after the recoil and before retracting back to the original position."),
         Editable(0f, 1000f, DecimalCount = 2, ValueStep = 0.1f)]
        public float RetractionDelay { get; set; }
        
        [Editable(VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" }),
         Serialize("0.0,0.0", IsPropertySaveable.Yes, description: "The range at which the barrel can rotate.", alwaysUseInstanceValues: true)]
        public Vector2 RotationLimits
        {
            get
            {
                return new Vector2(MathHelper.ToDegrees(minRotation), MathHelper.ToDegrees(maxRotation));
            }
            set
            {
                float newMinRotation = MathHelper.ToRadians(value.X);
                float newMaxRotation = MathHelper.ToRadians(value.Y);
                
                bool minRotationModified = MathHelper.Distance(newMinRotation, minRotation) > 0.02f;
                bool maxRotationModified = MathHelper.Distance(newMaxRotation, maxRotation) > 0.02f;
                
                // if only one rotation changes (when editing via text field), use the other one to clamp to max range
                if (minRotationModified && !maxRotationModified)
                {
                    newMinRotation = MathHelper.Clamp(newMinRotation, maxRotation - MathHelper.TwoPi, maxRotation);
                }
                else if (!minRotationModified && maxRotationModified)
                {
                    newMaxRotation = MathHelper.Clamp(newMaxRotation, minRotation, minRotation + MathHelper.TwoPi);
                }
                
                maxRotation = newMaxRotation;
                minRotation = newMinRotation;
                
                BarrelRotation = (minRotation + maxRotation) / 2;
#if CLIENT
                if (lightComponents != null)
                {
                    foreach (var light in lightComponents)
                    {
                        light.Rotation = BarrelRotation;
                        light.Light.Rotation = -BarrelRotation;
                    }
                }
#endif
            }
        }
        
        [Serialize(5.0f, IsPropertySaveable.No, description: "How much torque is applied to rotate the barrel when the item is used by a character with insufficient skills to operate it. Higher values make the barrel rotate faster."), 
         Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringStiffnessLowSkill { get; private set; }
        
        [Serialize(2.0f, IsPropertySaveable.No, description: "How much torque is applied to rotate the barrel when the item is used by a character with sufficient skills to operate it. Higher values make the barrel rotate faster."),
         Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringStiffnessHighSkill { get; private set; }

        [Serialize(50.0f, IsPropertySaveable.No, description: "How much torque is applied to resist the movement of the barrel when the item is used by a character with insufficient skills to operate it. Higher values make the aiming more \"snappy\", stopping the barrel from swinging around the direction it's being aimed at."),
         Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringDampingLowSkill { get; private set; }
        
        [Serialize(10.0f, IsPropertySaveable.No, description: "How much torque is applied to resist the movement of the barrel when the item is used by a character with sufficient skills to operate it. Higher values make the aiming more \"snappy\", stopping the barrel from swinging around the direction it's being aimed at."), 
         Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringDampingHighSkill { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Maximum angular velocity of the barrel when used by a character with insufficient skills to operate it."), 
         Editable(0.0f, 100.0f, DecimalCount = 2)]
        public float RotationSpeedLowSkill { get; private set; }
        
        [Serialize(5.0f, IsPropertySaveable.No, description: "Maximum angular velocity of the barrel when used by a character with sufficient skills to operate it."), 
         Editable(0.0f, 100.0f, DecimalCount = 2)]
        public float RotationSpeedHighSkill { get; private set; }
        
        [Serialize("0,0,0,0", IsPropertySaveable.Yes, description: "Optional screen tint color when the item is being operated (R,G,B,A)."), 
         Editable(TransferToSwappedItem = true)]
        public Color HudTint { get; set; }
        
        [Header(localizedTextTag: "sp.turret.AutoOperate.propertyheader")]
        [Serialize(false, IsPropertySaveable.Yes, description: "Should the turret operate automatically using AI targeting? Comes with some optional random movement that can be adjusted below."),
         Editable(TransferToSwappedItem = true)]
        public bool AutoOperate { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Can the Auto Operate functionality be enabled using signals to the turret?"),
            Editable(TransferToSwappedItem = true)]
        public bool AllowAutoOperateWithWiring { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, description: "[Auto Operate] How much the turret should adjust the aim off the target randomly instead of tracking the target perfectly? In Degrees."),
         Editable(TransferToSwappedItem = true)]
        public float RandomAimAmount { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, description: "[Auto Operate] How often the turret should adjust the aim randomly instead of tracking the target perfectly? Minimum wait time, in seconds."),
         Editable(TransferToSwappedItem = true)]
        public float RandomAimMinTime { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, description: "[Auto Operate] How often the turret should adjust the aim randomly instead of tracking the target perfectly? Maximum wait time, in seconds."),
         Editable(TransferToSwappedItem = true)]
        public float RandomAimMaxTime { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret move randomly while idle?"),
         Editable(TransferToSwappedItem = true)]
        public bool RandomMovement { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret have a delay while targeting targets or always aim prefectly?"),
         Editable(TransferToSwappedItem = true)]
        public bool AimDelay { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret target characters in general?"),
         Editable(TransferToSwappedItem = true)]
        public bool TargetCharacters { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret target all monsters?"),
         Editable(TransferToSwappedItem = true)]
        public bool TargetMonsters { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret target all humans (or creatures in the same group, like pets)?"),
         Editable(TransferToSwappedItem = true)]
        public bool TargetHumans { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret target other submarines?"),
         Editable(TransferToSwappedItem = true)]
        public bool TargetSubmarines { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "[Auto Operate] Should the turret target items?"),
         Editable(TransferToSwappedItem = true)]
        public bool TargetItems { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "[Auto Operate] Group or SpeciesName that the AI ignores when the turret is operated automatically."), 
         Editable(TransferToSwappedItem = true)]
        public Identifier FriendlyTag { get; private set; }

        [Serialize("OwnSub", IsPropertySaveable.Yes, description: "[Auto Operate] Team that the turret considers friendly."),
         Editable(TransferToSwappedItem = true)]
        public TeamType FriendlyTeamType { get; private set; }

        public enum TeamType
        {
            OwnSub,
            Team1,
            Team2,
            FriendlyNPC,
            NoneTeam
        }
        
        [Serialize("0,0", IsPropertySaveable.No, description: "The position of the barrel relative to the upper left corner of the base sprite (in pixels)."), Editable(ValueStep = 1, DecimalCount = 0)]
        public Vector2 BarrelPos
        {
            get 
            { 
                return barrelPos; 
            }
            set
            { 
                barrelPos = value;
                UpdateTransformedBarrelPos();
            }
        }
        

        [Serialize(0f, IsPropertySaveable.No, description: "The time required for a charge-type turret to charge up before able to fire."), Editable(ValueStep = 1, DecimalCount = 1)]
        public float MaxChargeTime { get; private set; }

        /// <summary>
        /// <see cref="MaxChargeTime"/> with the multiplier of the currently loaded ammo applied.
        /// </summary>
        public float CurrentMaxChargeTime =>
            MaxChargeTime <= 0 ? 0 : MaxChargeTime * (GetActiveTurretAmmo()?.MaxChargeTimeMultiplier ?? 1.0f);

        [Serialize("0,0", IsPropertySaveable.No, description: "The projectile launching location relative to transformed barrel position (in pixels)."), Editable(ValueStep = 1, DecimalCount = 0)]
        public Vector2 FiringOffset { get; set; }

        private bool flipFiringOffset;
        [Serialize(false, IsPropertySaveable.No, description: "If enabled, the firing offset will alternate from left to right (i.e. flipping the x-component of the offset each shot.)"), Editable]
        public bool AlternatingFiringOffset { get; set; }
        
        [Serialize(0.0f, IsPropertySaveable.No, description: "The impulse applied to the physics body of the projectile (the higher the impulse, the faster the projectiles are launched)."), Editable]
        public float LaunchImpulse { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Random spread applied to the firing angle of the projectiles (in degrees)."), Editable]
        public float Spread { get; set; }

        #endregion

        private const string SetAutoOperateConnection = "set_auto_operate";
        private const string ToggleAutoOperateConnection = "toggle_auto_operate";

        public Turret(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;

            useFixedFiringPos = element.GetAttribute(nameof(FixedFiringPos)) != null;
                        
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "barrelsprite":
                        barrelSprite = new Sprite(subElement);
                        TryLoadAsChargeSprite(barrelSprite, subElement);
                        break;
                    case "railsprite":
                        railSprite = new Sprite(subElement);
                        TryLoadAsChargeSprite(railSprite, subElement);
                        break;
                    case "barrelspritebroken":
                        barrelSpriteBroken = new Sprite(subElement);
                        break;
                    case "railspritebroken":
                        railSpriteBroken = new Sprite(subElement);
                        break;
                    case "chargesprite":
                        LoadAsChargeSprite(new Sprite(subElement), subElement);
                        break;
                    case "spinningbarrelsprite":
                        int spriteCount = subElement.GetAttributeInt("spriteamount", 1);
                        for (int i = 0; i < spriteCount; i++)
                        {
                            spinningBarrelSprites.Add(new Sprite(subElement));
                        }
                        break;
                }
            }
            item.IsShootable = true;
            item.RequireAimToUse = false;
            isSlowTurret = item.HasTag("slowturret".ToIdentifier());
            InitProjSpecific(element);
            
            void TryLoadAsChargeSprite(Sprite s, ContentXElement e)
            {
                if (IsChargeSprite(e))
                {
                    LoadAsChargeSprite(s, e);
                }
            }
            
            static bool IsChargeSprite(ContentXElement e) => e.GetAttribute(nameof(ChargeSprite.ChargeOffset)) != null || e.GetAttribute("chargetarget") != null|| e.GetAttribute(nameof(ChargeSprite.ChargeRotation)) != null;
            
            void LoadAsChargeSprite(Sprite s, ContentXElement e)
            {
                Vector2 offset = e.GetAttributeVector2(nameof(ChargeSprite.ChargeOffset), Vector2.Zero);
                if (offset == Vector2.Zero)
                {
                    offset = e.GetAttributeVector2("chargetarget", Vector2.Zero);
                }
                float rotation = MathHelper.ToRadians(e.GetAttributeFloat(nameof(ChargeSprite.ChargeRotation), 0f));
                chargeSprites.Add(new ChargeSprite(s, offset, rotation));
            }
        }

        partial void InitProjSpecific(ContentXElement element);

        private void UpdateTransformedBarrelPos()
        {
            transformedBarrelPos = GetTransformedBarrelPos(item.Rotation);
            transformedFixedFiringPos = GetTransformedFixedFiringPos(item.Rotation);
#if CLIENT
            item.ResetCachedVisibleSize();
#endif
            prevBaseRotation = item.Rotation;
            prevScale = item.Scale;
        }

        /// <summary>
        /// Returns the barrel position (origin of the barrel), taking the scale and current rotation of the turret into account.
        /// Relative to the top-left corner of the item's rect.
        /// </summary>
        public Vector2 GetTransformedBarrelPos(float rotationRad)
        {
            return MathUtils.RotatePointAroundTarget(barrelPos * item.Scale, new Vector2(item.Rect.Width / 2, item.Rect.Height / 2), MathHelper.ToRadians(rotationRad));
        }

        public Vector2 GetTransformedBarrelWorldPosition()
        {
            return item.WorldRect.Location.ToVector2() + GetTransformedBarrelPos(item.Rotation).FlipY();
        }

        /// <summary>
        /// Returns the barrel position (origin of the barrel), taking the scale and current rotation of the turret into account.
        /// Relative to the top-left corner of the item's rect.
        /// </summary>
        public Vector2 GetTransformedFixedFiringPos(float rotationRad)
        {
            return MathUtils.RotatePointAroundTarget(FixedFiringPos * item.Scale, new Vector2(item.Rect.Width / 2, item.Rect.Height / 2), MathHelper.ToRadians(rotationRad));
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            if (loadedRotationLimits.HasValue) { RotationLimits = loadedRotationLimits.Value; }
            if (loadedBaseRotation.HasValue) { BaseRotation = loadedBaseRotation.Value; }
            if (loadedFriendlyTeamType.HasValue) { FriendlyTeamType = loadedFriendlyTeamType.Value; }
            targetRotation = BarrelRotation;
            UpdateTransformedBarrelPos();
            if (!AllowAutoOperateWithWiring && 
                Screen.Selected is { IsEditor: false })
            {
                // If the turret is not set to auto operate and the auto operate connections haven't been wired to anything,
                // don't allow changing the state with wirings.
                foreach (ConnectionPanel connectionPanel in Item.GetComponents<ConnectionPanel>())
                {
                    connectionPanel.Connections.RemoveAll(c => c.Name is ToggleAutoOperateConnection or SetAutoOperateConnection && c.Wires.None());
                }
            }
        }

        private void FindLightComponents()
        {
            if (lightComponents != null)
            {
                // Can't run again, because of reparenting.
                return;
            }
            foreach (LightComponent lc in item.GetComponents<LightComponent>())
            {
                // Only make the Turret control the LightComponents that are it's children. So it'd be possible to for example have some extra lights on the turret that don't rotate with it.
                if (lc?.Parent == this)
                {
                    lightComponents ??= new List<LightComponent>();
                    lightComponents.Add(lc);
                }
            }

#if CLIENT
            if (lightComponents != null)
            {
                foreach (var light in lightComponents)
                {
                    // We want the turret to control the state of the LightComponent, not tie it's state to the state of the Turret (the light can be inactive even if the turret is active)
                    light.Parent = null;
                    light.Rotation = BarrelRotation - item.RotationRad;
                    light.Light.Rotation = -BarrelRotation;
                    //turret lights are high-prio (don't want the lights to disappear when you're fighting something)
                    light.Light.PriorityMultiplier *= 10.0f;
                }
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            this.cam = cam;

            if (reload > 0.0f) { reload -= deltaTime; }
            if (!MathUtils.NearlyEqual(item.Rotation, prevBaseRotation) || !MathUtils.NearlyEqual(item.Scale, prevScale))
            {
                UpdateTransformedBarrelPos();
            }

            if (User is { Removed: true })
            {
                User = null;
            }
            else
            {
                resetUserTimer -= deltaTime;
                resetActiveUserTimer -= deltaTime;
                if (resetActiveUserTimer <= 0.0f)
                {
                    hasBeenActivelyUsed = false;
                }
                if (resetUserTimer <= 0.0f)
                {
                    User = null;
                }
            }

#if SERVER
            //the rotation was being controlled by signals, but they stopped coming (e.g. a circuit routed them elsewhere):
            //sync the rotation the turret was left at, because it can differ between clients depending on which signals they happened to receive
            if (receivedPositionSignalPrevFrame && !receivedPositionSignal)
            {
                item.CreateServerEvent(this);
            }
#endif
            receivedPositionSignalPrevFrame = receivedPositionSignal;
            receivedPositionSignal = false;

            activeProjectiles.RemoveAll(p => p.Item.Removed || !p.ProjectileActive);

            ApplyStatusEffects(ActionType.OnActive, deltaTime);

            item.SendSignal(((int)MathHelper.ToDegrees(BarrelRotation)).ToString(), "position_out");

            float previousChargeTime = currentChargeTime;
            float maxChargeTime = CurrentMaxChargeTime;

            if (SingleChargedShot && reload > 0f)
            {
                // single charged shot guns will decharge after firing
                // for cosmetic reasons, this is done by lerping in half the reload time
                float reloadDuration = lastLaunchReload > 0.0f ? lastLaunchReload : Reload;
                currentChargeTime = reloadDuration > 0.0f ?
                    Math.Clamp(maxChargeTime * (reload / reloadDuration - 0.5f), 0f, maxChargeTime) :
                    0.0f;
            }
            else
            {
                float chargeDeltaTime = tryingToCharge ? deltaTime : -deltaTime;
                if (chargeDeltaTime > 0f && User != null)
                {
                    chargeDeltaTime *= 1f + User.GetStatValue(StatTypes.TurretChargeSpeed);
                }
                currentChargeTime = Math.Clamp(currentChargeTime + chargeDeltaTime, 0f, maxChargeTime);

            }
            tryingToCharge = false;

            if (currentChargeTime == 0f)
            {
                currentChargingState = ChargingState.Inactive;
            } 
            else if (currentChargeTime < previousChargeTime)
            {
                currentChargingState = ChargingState.WindingDown;
            } 
            else
            {
                // if we are charging up or at maxed charge, remain winding up
                currentChargingState = ChargingState.WindingUp;
                ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, user: User);
            }

            UpdateProjSpecific(deltaTime);

            if (MathUtils.NearlyEqual(minRotation, maxRotation))
            {
                UpdateLightComponents();
                return;
            }

            float targetMidDiff = MathHelper.WrapAngle(targetRotation - (minRotation + maxRotation) / 2.0f);

            float maxDist = (maxRotation - minRotation) / 2.0f;

            if (Math.Abs(targetMidDiff) > maxDist)
            {
                targetRotation = (targetMidDiff < 0.0f) ? minRotation : maxRotation;
            }

            float degreeOfSuccess = User == null ? 0.5f : DegreeOfSuccess(User);
            if (degreeOfSuccess < 0.5f) { degreeOfSuccess *= degreeOfSuccess; } //the ease of aiming drops quickly with insufficient skill levels
            float springStiffness = MathHelper.Lerp(SpringStiffnessLowSkill, SpringStiffnessHighSkill, degreeOfSuccess);
            float springDamping = MathHelper.Lerp(SpringDampingLowSkill, SpringDampingHighSkill, degreeOfSuccess);
            float rotationSpeed = MathHelper.Lerp(RotationSpeedLowSkill, RotationSpeedHighSkill, degreeOfSuccess);
            if (maxChargeTime > 0)
            {
                rotationSpeed *= MathHelper.Lerp(1f, FiringRotationSpeedModifier, MathUtils.EaseIn(currentChargeTime / maxChargeTime));
            }

            // Do not increase the weapons skill when operating a turret in an outpost level
            if (User?.Info != null && (GameMain.GameSession?.Campaign == null || !Level.IsLoadedFriendlyOutpost))
            {
                User.Info.ApplySkillGain(
                    Tags.WeaponsSkill,
                    SkillSettings.Current.SkillIncreasePerSecondWhenOperatingTurret * deltaTime);
            }

            float rotMidDiff = MathHelper.WrapAngle(BarrelRotation - (minRotation + maxRotation) / 2.0f);

            float targetRotationDiff = MathHelper.WrapAngle(targetRotation - BarrelRotation);

            if ((maxRotation - minRotation) < MathHelper.TwoPi)
            {
                float targetRotationMaxDiff = MathHelper.WrapAngle(targetRotation - maxRotation);
                float targetRotationMinDiff = MathHelper.WrapAngle(targetRotation - minRotation);

                if (Math.Abs(targetRotationMaxDiff) < Math.Abs(targetRotationMinDiff) &&
                    rotMidDiff < 0.0f &&
                    targetRotationDiff < 0.0f)
                {
                    targetRotationDiff += MathHelper.TwoPi;
                }
                else if (Math.Abs(targetRotationMaxDiff) > Math.Abs(targetRotationMinDiff) &&
                    rotMidDiff > 0.0f &&
                    targetRotationDiff > 0.0f)
                {
                    targetRotationDiff -= MathHelper.TwoPi;
                }
            }

            angularVelocity += 
                (targetRotationDiff * springStiffness - angularVelocity * springDamping) * deltaTime;
            angularVelocity = MathHelper.Clamp(angularVelocity, -rotationSpeed, rotationSpeed);

            BarrelRotation += angularVelocity * deltaTime;

            rotMidDiff = MathHelper.WrapAngle(BarrelRotation - (minRotation + maxRotation) / 2.0f);

            if (rotMidDiff < -maxDist)
            {
                BarrelRotation = minRotation;
                angularVelocity *= -0.5f;
            }
            else if (rotMidDiff > maxDist)
            {
                BarrelRotation = maxRotation;
                angularVelocity *= -0.5f;
            }

            if (aiFindTargetTimer > 0.0f)
            {
                aiFindTargetTimer -= deltaTime;
            }

            UpdateLightComponents();

            if (AutoOperate && !IsBeingActivelyUsed)
            {
                UpdateAutoOperate(deltaTime, ignorePower: false);
            }
        }

        public void UpdateLightComponents()
        {
            if (lightComponents != null)
            {
                foreach (var light in lightComponents)
                {
                    light.Rotation = BarrelRotation - item.RotationRad;
                }
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        private bool isUseBeingCalled;

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!characterUsable && character != null) { return false; }
            //prevent an infinite loop if launching triggers a StatusEffect that Uses this item
            if (isUseBeingCalled) { return false;  }

            isUseBeingCalled = true;
            bool wasSuccessful = TryLaunch(deltaTime, character);
            isUseBeingCalled = false;
            return wasSuccessful;
        }

        public float GetPowerRequiredToShoot()
        {
            float powerCost = powerConsumption * (GetActiveTurretAmmo()?.PowerConsumptionMultiplier ?? 1.0f);
            if (User != null)
            {
                powerCost /= (1 + User.GetStatValue(StatTypes.TurretPowerCostReduction));
            }
            return powerCost;
        }

        public bool HasPowerToShoot()
        {
            return GetAvailableInstantaneousBatteryPower() >= GetPowerRequiredToShoot();
        }

        public Vector2 GetFiringDirection()
        {
            float angle = FiringRotation;
            return new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        }

        /// <summary>
        /// NOTE: this may not be the same as the firing direction if the turret uses <see cref="FixedFiringPos"/> (i.e. fires in a fixed direction regardless of the barrel).
        /// </summary>
        private Vector2 GetBarrelDirection()
        {
            float angle = BarrelRotation;
            return new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        }

        private bool TryLaunch(float deltaTime, Character character = null, bool ignorePower = false)
        {
            tryingToCharge = true;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }

            if (currentChargeTime < CurrentMaxChargeTime) { return false; }

            if (reload > 0.0f) { return false; }

            if (MaxActiveProjectiles >= 0)
            {
                if (activeProjectiles.Count >= MaxActiveProjectiles)
                {
                    return false;
                }
            }
            
            if (!ignorePower)
            {
                if (!HasPowerToShoot())
                {
#if CLIENT
                    if (!flashLowPower && character != null && character == Character.Controlled)
                    {
                        flashLowPower = true;
                        SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                    }
#endif
                    reload = Math.Max(reload, ReloadWhenOutOfPower);
                    return false;
                }
            }

            //are all the linked loaders missing the items they're configured to require? Null indicates there were no linked loaders.
            bool linkedLoadersMissingRequiredItems = true;
            Projectile launchedProjectile = null;
            bool loaderBroken = false;
            float tinkeringStrength = 0f;

            for (int i = 0; i < ProjectileCount; i++)
            {
                var projectiles = GetLoadedProjectiles();
                if (projectiles.Any())
                {
                    ItemContainer projectileContainer = projectiles.First().Item.Container?.GetComponent<ItemContainer>();
                    if (projectileContainer != null && projectileContainer.Item != item)
                    {
                        //user needs to be null because the ammo boxes shouldn't be directly usable by characters
                        projectileContainer?.Item.Use(deltaTime, user: null, userForOnUsedEvent: User);
                    }
                }
                else
                {
                    for (int j = 0; j < item.linkedTo.Count; j++)
                    {
                        var e = item.linkedTo[(j + currentLoaderIndex) % item.linkedTo.Count];
                        //use linked projectile containers in case they have to react to the turret being launched somehow
                        //(play a sound, spawn more projectiles)
                        if (e is not Item linkedItem) { continue; }
                        if (!item.Prefab.IsLinkAllowed(e.Prefab)) { continue; }
                        if (linkedItem.Condition <= 0.0f)
                        {
                            loaderBroken = true;
                            continue;
                        }
                        if (tryUseProjectileContainer(linkedItem)) { break; }
                    }
                    tryUseProjectileContainer(item);

                    bool tryUseProjectileContainer(Item containerItem)
                    {
                        ItemContainer projectileContainer = containerItem.GetComponent<ItemContainer>();
                        if (projectileContainer != null)
                        {                            
                            if (linkedLoadersMissingRequiredItems)
                            { 
                                linkedLoadersMissingRequiredItems = !projectileContainer.HasRequiredContainedItems(user: null, addMessage: false);                        
                            }
                            containerItem.Use(deltaTime, user: null, userForOnUsedEvent: User);
                            projectiles = GetLoadedProjectiles();
                            if (projectiles.Any()) { return true; }
                        }
                        return false;
                    }
                }

                if ((projectiles.None() && !LaunchWithoutProjectile) ||
                    (linkedLoadersMissingRequiredItems && !LaunchWithoutRequiredItemsInLoaders))
                {
                    //coilguns spawns ammo in the ammo boxes with the OnUse statuseffect when the turret is launched,
                    //causing a one frame delay before the gun can be launched (or more in multiplayer where there may be a longer delay)
                    //  -> attempt to launch the gun multiple times before showing the "no ammo" flash
                    failedLaunchAttempts++;
#if CLIENT
                    if (!flashNoAmmo && !flashLoaderBroken && character != null && character == Character.Controlled && failedLaunchAttempts > 20)
                    {
                        if (loaderBroken)
                        {
                            flashLoaderBroken = true;
                        }
                        else
                        {
                            flashNoAmmo = true;
                        }
                        failedLaunchAttempts = 0;
                        SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                    }
#endif
                    return false;
                }
                failedLaunchAttempts = 0;

                LastFiredTime = (float)Timing.TotalTimeUnpaused;

                foreach (MapEntity e in item.linkedTo)
                {
                    if (e is not Item linkedItem) { continue; }
                    if (!((MapEntity)item).Prefab.IsLinkAllowed(e.Prefab)) { continue; }
                    if (linkedItem.GetComponent<Repairable>() is Repairable repairable && repairable.IsTinkering && linkedItem.HasTag(Tags.TurretAmmoSource))
                    {
                        tinkeringStrength = repairable.TinkeringStrength;
                    }
                }

                if (!ignorePower)
                {
                    var batteries = GetDirectlyConnectedBatteries().Where(static b => !b.OutputDisabled && b.Charge > 0.0001f && b.MaxOutPut > 0.0001f);
                    float neededPower = GetPowerRequiredToShoot();
                    // tinkering is currently not factored into the common method as it is checked only when shooting
                    // but this is a minor issue that causes mostly cosmetic woes. might still be worth refactoring later
                    neededPower /= 1f + (tinkeringStrength * TinkeringPowerCostReduction);
                    while (neededPower > 0.0001f && batteries.Any())
                    {
                        float takePower = neededPower / batteries.Count();
                        takePower = Math.Min(takePower, batteries.Min(b => Math.Min(b.Charge * 3600.0f, b.MaxOutPut)));
                        foreach (PowerContainer battery in batteries)
                        {
                            neededPower -= takePower;
                            battery.Charge -= takePower / 3600.0f;
#if SERVER
                            battery.Item.CreateServerEvent(battery);                        
#endif
                        }
                    }
                }

                launchedProjectile = projectiles.FirstOrDefault();
                Item container = launchedProjectile?.Item.Container;
                if (container != null)
                {
                    var repairable = launchedProjectile?.Item.Container.GetComponent<Repairable>();
                    if (repairable != null)
                    {
                        repairable.LastActiveTime = (float)Timing.TotalTime + 1.0f;
                    }
                }

                if (launchedProjectile != null || LaunchWithoutProjectile)
                {
                    if (launchedProjectile?.Item.GetComponent<Rope>() != null &&
                        lastProjectile?.Item.GetComponent<Rope>() is { SnapWhenWeaponFiredAgain: true } rope)
                    {
                        rope.Snap();
                    }

                    if (projectiles.Any())
                    {
                        foreach (Projectile projectile in projectiles)
                        {
                            Launch(projectile.Item, character, tinkeringStrength: tinkeringStrength);
                        }
                    }
                    else
                    {
                        Launch(null, character, tinkeringStrength: tinkeringStrength);
                    }
                    if (item.AiTarget != null)
                    {
                        item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
                        // Turrets also have a light component, which handles the sight range.
                    }
                    if (container != null)
                    {
                        ShiftItemsInProjectileContainer(container.GetComponent<ItemContainer>());
                    }
                    if (item.linkedTo.Count > 0)
                    {
                        currentLoaderIndex = (currentLoaderIndex + 1) % item.linkedTo.Count;
                    }
                }
            }

            lastProjectile = launchedProjectile;

#if SERVER
            if (character != null && launchedProjectile != null)
            {
                string msg = GameServer.CharacterLogName(character) + " launched " + item.Name + " (projectile: " + launchedProjectile.Item.Name;
                var containedItems = launchedProjectile.Item.ContainedItems;
                if (containedItems == null || !containedItems.Any())
                {
                    msg += ")";
                }
                else
                {
                    msg += ", contained items: " + string.Join(", ", containedItems.Select(i => i.Name)) + ")";
                }
                GameServer.Log(msg, ServerLog.MessageType.ItemInteraction);
            }
#endif

            return true;
        }

        private readonly struct EventData : IEventData
        {
            public readonly Item Projectile;
            
            public EventData(Item projectile, Turret turret)
            {
                Projectile = projectile;
            }
        }
        
        private void Launch(Item projectile, Character user = null, float? launchRotation = null, float tinkeringStrength = 0f)
        {
            //resolve the ammo before launching: launching removes the projectile from the ammo box, after which an empty box would no longer count as the active ammo
            LastLaunchedTurretAmmo = GetActiveTurretAmmo();
            reload = Reload * (LastLaunchedTurretAmmo?.ReloadMultiplier ?? 1.0f);
            if (ShotsPerBurst > 1)
            {
                shotCounter++;
                if (shotCounter >= ShotsPerBurst)
                {
                    reload += DelayBetweenBursts;
                    shotCounter = 0;
                }
            }
            reload /= 1f + (tinkeringStrength * TinkeringReloadDecrease);

            if (user != null)
            {
                reload /= 1 + user.GetStatValue(StatTypes.TurretAttackSpeed);
            }
            lastLaunchReload = reload;

#if SERVER
            item.CreateServerEvent(this, new EventData(projectile, this));
#endif
            if (projectile != null)
            {
                if (AlternatingFiringOffset)
                {
                    flipFiringOffset = !flipFiringOffset;
                }
                projectile.Drop(null, setTransform: false);
                if (projectile.body != null) 
                {                 
                    projectile.body.Dir = 1.0f;
                    projectile.body.ResetDynamics();
                    projectile.body.Enabled = true;
                }
                
                float spread = MathHelper.ToRadians(Spread) * Rand.Range(-0.5f, 0.5f);

                Vector2 launchPos = ConvertUnits.ToSimUnits(GetFiringPosition());

                //check if there's some other sub between the turret's origin and the launch pos,
                //and if so, launch at the intersection of the turret and the sub to prevent the projectile from spawning inside the other sub
                Body pickedBody = Submarine.PickBody(ConvertUnits.ToSimUnits(item.WorldPosition), launchPos, null, Physics.CollisionWall, allowInsideFixture: true,
                   customPredicate: (Fixture f) =>
                   {
                       return f.Body.UserData is not Submarine sub || sub != item.Submarine;
                   });
                if (pickedBody != null)
                {
                    launchPos = Submarine.LastPickedPosition;
                }
                projectile.SetTransform(launchPos, -(launchRotation ?? FiringRotation) + spread);
                projectile.UpdateTransform();
                projectile.Submarine = projectile.body?.Submarine;

                Projectile projectileComponent = projectile.GetComponent<Projectile>();
                if (projectileComponent != null)
                {
                    activeProjectiles.Add(projectileComponent);
                    TryDetermineProjectileSpeed(projectileComponent);
                    projectileComponent.Launcher = item;
                    projectileComponent.Attacker = projectileComponent.User = user;
                    if (projectileComponent.Attack != null)
                    {
                        projectileComponent.Attack.DamageMultiplier = (1f * DamageMultiplier) + (TinkeringDamageIncrease * tinkeringStrength);
                    }
                    projectileComponent.Use(null, LaunchImpulse);
                    if (item.GetComponent<TriggerComponent>() is { } trigger)
                    {
                        projectileComponent.IgnoredBodies.Add(trigger.PhysicsBody.FarseerBody);
                    }
                    projectile.GetComponent<Rope>()?.Attach(item, projectile);
                    projectileComponent.User = user;

                    if (item.Submarine != null && projectile.body != null)
                    {
                        Vector2 velocitySum = item.Submarine.PhysicsBody.LinearVelocity + projectile.body.LinearVelocity;
                        if (velocitySum.LengthSquared() < NetConfig.MaxPhysicsBodyVelocity * NetConfig.MaxPhysicsBodyVelocity * 0.9f)
                        {
                            projectile.body.LinearVelocity = velocitySum;
                        }
                    }
                }

                projectile.Container?.RemoveContained(projectile);
            }

            //mark the item as having been used, even if the launching was done with signals (which doesn't go through the usual path through Item.Use)
            WasUsed = true;

            //if the item has a reload time, treat using as a single instantaneous event and don't use deltaTime
            //otherwise consider it happening over time, and handle deltaTime normally
            float deltaTime = Reload > 0.0f ? 1.0f : (float)Timing.Step;

            ApplyStatusEffects(ActionType.OnUse, deltaTime, user: user);
            LaunchProjSpecific();
        }

        private void TryDetermineProjectileSpeed(Projectile projectile)
        {
            if (projectile != null && !projectile.Hitscan)
            {
                projectileSpeed =
                    ConvertUnits.ToDisplayUnits(
                        MathHelper.Clamp((projectile.LaunchImpulse + LaunchImpulse) / projectile.Item.body.Mass, MinimumProjectileVelocityForAimAhead, NetConfig.MaxPhysicsBodyVelocity));
            }
        }

        partial void LaunchProjSpecific();

        private static void ShiftItemsInProjectileContainer(ItemContainer container)
        {
            if (container == null) { return; }
            bool moved;
            do
            {
                moved = false;
                for (int i = 1; i < container.Capacity; i++)
                {
                    if (container.Inventory.GetItemAt(i) is Item item1 && container.Inventory.CanBePutInSlot(item1, i - 1))
                    {
                        if (container.Inventory.TryPutItem(item1, i - 1, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: true))
                        {
                            moved = true;
                        }
                    }
                }
            } while (moved);
        }

        private float waitTimer;
        private float randomAimTimer;

        private float prevTargetRotation;
        private float updateTimer;
        private bool updatePending;

        private bool receivedPositionSignal, receivedPositionSignalPrevFrame;

        private float GetTargetPriorityModifier() => currentChargingState == ChargingState.WindingUp ? 10f : AICurrentTargetPriorityMultiplier;

        public void UpdateAutoOperate(float deltaTime, bool ignorePower, Identifier friendlyTag = default, bool ignoreTeam = false)
        {
            User = null;
            if (!ignorePower && !HasPowerToShoot())
            {
                return;
            }

            IsActive = true;

            if (friendlyTag.IsEmpty)
            {
                friendlyTag = FriendlyTag;
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                return;
            }

            if (updatePending)
            {
                if (updateTimer < 0.0f)
                {
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    prevTargetRotation = targetRotation;
                    updateTimer = 0.25f;
                    updatePending = false;
                }
                updateTimer -= deltaTime;
            }

            if (waitTimer > 0)
            {
                waitTimer -= deltaTime;
                return;
            }
            Submarine closestSub = null;
            float maxDistance = 10000.0f;
            float shootDistance = AIRange;
            ISpatialEntity target = null;
            float closestDist = shootDistance * shootDistance;
            if (TargetCharacters)
            {
                foreach (var character in Character.CharacterList)
                {
                    if (!IsValidTarget(character)) { continue; }
                    float priority = isSlowTurret ? character.Params.AISlowTurretPriority : character.Params.AITurretPriority;
                    if (priority <= 0) { continue; }
                    if (!IsValidTargetForAutoOperate(character, friendlyTag, ignoreTeam)) { continue; }
                    float dist = Vector2.DistanceSquared(character.WorldPosition, item.WorldPosition);
                    if (dist > closestDist) { continue; }
                    if (!IsWithinAimingRadius(character.WorldPosition)) { continue; }
                    target = character;
                    if (currentTarget != null && target == currentTarget)
                    {
                        priority *= GetTargetPriorityModifier();
                    }
                    closestDist = dist / priority;
                }
            }
            if (TargetItems)
            {
                foreach (Item targetItem in Item.TurretTargetItems)
                {
                    if (!IsValidTarget(targetItem)) { continue; }
                    float priority = isSlowTurret ? targetItem.Prefab.AISlowTurretPriority : targetItem.Prefab.AITurretPriority;
                    if (priority <= 0) { continue; }
                    float dist = Vector2.DistanceSquared(item.WorldPosition, targetItem.WorldPosition);
                    if (dist > closestDist) { continue; }
                    if (dist > shootDistance * shootDistance) { continue; }
                    if (!IsTargetItemCloseEnough(targetItem, dist)) { continue; }
                    if (!IsWithinAimingRadius(targetItem.WorldPosition)) { continue; }
                    target = targetItem;
                    if (currentTarget != null && target == currentTarget)
                    {
                        priority *= GetTargetPriorityModifier();
                    }
                    closestDist = dist / priority;
                }
            }
            if (TargetSubmarines)
            {
                if (target == null || target.Submarine != null)
                {
                    closestDist = maxDistance * maxDistance;
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub == Item.Submarine) { continue; }
                        if (sub.IsRespawnShuttle) { continue; }
                        if (!ignoreTeam && item.Submarine != null)
                        {
                            if (Character.IsOnFriendlyTeam(item.Submarine.TeamID, sub.TeamID)) { continue; }
                        }
                        float dist = Vector2.DistanceSquared(sub.WorldPosition, item.WorldPosition);
                        if (dist > closestDist) { continue; }
                        closestSub = sub;
                        closestDist = dist;
                    }
                    closestDist = shootDistance * shootDistance;
                    if (closestSub != null)
                    {
                        foreach (var hull in Hull.HullList)
                        {
                            if (!closestSub.IsEntityFoundOnThisSub(hull, true)) { continue; }
                            float dist = Vector2.DistanceSquared(hull.WorldPosition, item.WorldPosition);
                            if (dist > closestDist) { continue; }
                            // Don't check the angle, because it doesn't work on Thalamus spike. The angle check wouldn't be very important here anyway.
                            target = hull;
                            closestDist = dist;
                        }
                    }
                }
            }

            if (target == null && RandomMovement)
            {
                // Random movement while there's no target
                waitTimer = Rand.Value(Rand.RandSync.Unsynced) < 0.98f ? 0f : Rand.Range(1f, 2f);
                targetRotation = Rand.Range(minRotation, maxRotation);
                updatePending = true;
                return;
            }

            if (AimDelay)
            {
                if (RandomAimAmount > 0)
                {
                    if (randomAimTimer < 0)
                    {
                        // Random disorder or other flaw in the targeting.
                        randomAimTimer = Rand.Range(RandomAimMinTime, RandomAimMaxTime);
                        waitTimer = Rand.Range(0.25f, 1f);
                        float randomAim = MathHelper.ToRadians(RandomAimAmount);
                        targetRotation = MathUtils.WrapAngleTwoPi(targetRotation += Rand.Range(-randomAim, randomAim));
                        updatePending = true;
                        return;
                    }
                    else
                    {
                        randomAimTimer -= deltaTime;
                    }
                }
            }
            if (target == null) { return; }
            currentTarget = target;

            float angle = -MathUtils.VectorToAngle(target.WorldPosition - item.WorldPosition);
            targetRotation = MathUtils.WrapAngleTwoPi(angle);
            if (Math.Abs(targetRotation - prevTargetRotation) > 0.1f) { updatePending = true; }

            if (target is Hull targetHull)
            {
                Vector2 barrelDir = GetFiringDirection();
                if (!MathUtils.GetLineWorldRectangleIntersection(item.WorldPosition, item.WorldPosition + barrelDir * AIRange, targetHull.WorldRect, out _))
                {
                    return;
                }
                if (User != null)
                {
                    foreach (var otherCharacter in Character.GetFriendlyCrew(User))
                    {
                        if (otherCharacter.CurrentHull == targetHull)
                        {
                            User.Speak(TextManager.GetWithVariable("DialogFriendliesInTurretTargetHull", "[character]", otherCharacter.DisplayName).Value,
                                identifier: $"friendliesinturrettargethull{targetHull.ID}".ToIdentifier(),
                                minDurationBetweenSimilar: 30.0f);
                            return;
                        }
                    }
                }
            }
            else
            {
                if (!IsWithinAimingRadius(angle)) { return; }
                if (!IsPointingTowards(target.WorldPosition)) { return; }
            }
            Vector2 start = ConvertUnits.ToSimUnits(item.WorldPosition);
            Vector2 end = ConvertUnits.ToSimUnits(target.WorldPosition);

            bool doLineOfSightCheck = lastLineOfSightCheck.Time < Timing.TotalTimeUnpaused - LineOfSightCheckInterval;
            if (doLineOfSightCheck)
            {
                lastLineOfSightCheck.WorldTarget = CheckLineOfSight(start, end);
                lastLineOfSightCheck.Time = Timing.TotalTimeUnpaused;
            }

            // Check that there's not other entities that shouldn't be targeted (like a friendly sub) between us and the target.
            Body worldTarget = lastLineOfSightCheck.WorldTarget;
            bool shoot;
            if (target.Submarine != null)
            {
                if (doLineOfSightCheck)
                {
                    start -= target.Submarine.SimPosition;
                    end -= target.Submarine.SimPosition;
                    lastLineOfSightCheck.TransformedTarget = CheckLineOfSight(start, end);
                }
                shoot =
                    (worldTarget == null || CanShoot(worldTarget, friendlyTag, TargetSubmarines, ignoreTeam: ignoreTeam)) &&
                    CanShoot(lastLineOfSightCheck.TransformedTarget, friendlyTag, TargetSubmarines, ignoreTeam: ignoreTeam);
            }
            else
            {
                shoot = CanShoot(worldTarget, friendlyTag, TargetSubmarines, ignoreTeam: ignoreTeam);
            }
            if (shoot)
            {
                TryLaunch(deltaTime, ignorePower: ignorePower);
            }
        }

        public override bool CrewAIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            User = character;
            if (character.AIController.SelectedAiTarget?.Entity is Character previousTarget && previousTarget.IsDead)
            {
                if (previousTarget.LastAttacker == null || previousTarget.LastAttacker == character)
                {
                    character.Speak(TextManager.Get("DialogTurretTargetDead").Value,
                        identifier: $"killedtarget{previousTarget.ID}".ToIdentifier(),
                        minDurationBetweenSimilar: 5.0f);
                }
                character.AIController.SelectTarget(null);
            }

            bool keepAimingRegardlessOfPowerOrAmmo =
                AIKeepAimingWhenProjectilesActive &&
                ActiveProjectiles.Any(p => p is { IsActive: true, DeactivationTime: > 0, DeactivationTimer: > 0 });

            bool canShoot = !AIMaxActiveProjectilesReached && HasPowerToShoot();
            if (!canShoot && !keepAimingRegardlessOfPowerOrAmmo)
            {
                float lowestCharge = 0.0f;
                PowerContainer batteryToLoad = null;
                foreach (PowerContainer battery in GetDirectlyConnectedBatteries())
                {
                    if (!battery.Item.IsInteractable(character)) { continue; }
                    if (battery.OutputDisabled) { continue; }
                    if (batteryToLoad == null || battery.Charge < lowestCharge)
                    {
                        batteryToLoad = battery;
                        lowestCharge = battery.Charge;
                    }
                    if (battery.Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(battery.Item, character, allowInAnySub: true))
                    {
                        if (battery.Item.Repairables.Average(r => r.DegreeOfSuccess(character)) > 0.4f)
                        {
                            objective.AddSubObjective(new AIObjectiveRepairItem(character, battery.Item, objective.objectiveManager, isPriority: true));
                            return false;
                        }
                        else
                        {
                            character.Speak(TextManager.Get("DialogSupercapacitorIsBroken").Value,
                                identifier: "supercapacitorisbroken".ToIdentifier(),
                                minDurationBetweenSimilar: 30.0f);
                        }
                    }
                }
                if (batteryToLoad == null) { return true; }
                if (batteryToLoad.RechargeSpeed < batteryToLoad.MaxRechargeSpeed * 0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, objective.objectiveManager, option: Identifier.Empty, requireEquip: false));                    
                    return false;
                }
                if (lowestCharge <= 0 && batteryToLoad.Item.ConditionPercentage > 0)
                {
                    character.Speak(TextManager.Get("DialogTurretHasNoPower").Value,
                        identifier: "turrethasnopower".ToIdentifier(),
                        minDurationBetweenSimilar: 30.0f);
                }
            }

            int usableProjectileCount = 0;
            int maxProjectileCount = 0;
            if (item.HasTag(Tags.TurretAmmoSource) && item.IsInteractable(character))
            {
                CheckContainerForAmmo(item);
            }
            foreach (MapEntity e in item.linkedTo)
            {
                if (!item.IsInteractable(character)) { continue; }
                if (!((MapEntity)item).Prefab.IsLinkAllowed(e.Prefab)) { continue; }
                if (e is Item projectileContainer)
                {
                    CheckContainerForAmmo(projectileContainer);
                }
            }

            void CheckContainerForAmmo(Item projectileContainer)
            {
                var container = projectileContainer.GetComponent<ItemContainer>();
                if (container != null)
                {
                    maxProjectileCount += container.Capacity;
                    var projectiles = projectileContainer.ContainedItems.Where(it => it.Condition > 0.0f);
                    var firstProjectile = projectiles.FirstOrDefault();

                    if (firstProjectile?.Prefab != previousAmmo?.Prefab)
                    {
                        //assume the projectiles are infinitely fast (no aiming ahead of the target) if we can't find projectiles to calculate the speed based on,
                        //and if the projectile type isn't the same as before
                        projectileSpeed = float.PositiveInfinity;
                    }
                    previousAmmo = firstProjectile;
                    if (projectiles.Any())
                    {
                        var projectile =
                            firstProjectile.GetComponent<Projectile>() ??
                            firstProjectile.ContainedItems.FirstOrDefault()?.GetComponent<Projectile>();
                        TryDetermineProjectileSpeed(projectile);
                        usableProjectileCount += projectiles.Count();
                    }
                }
            }

            if (usableProjectileCount == 0 && !keepAimingRegardlessOfPowerOrAmmo)
            {
                ItemContainer container = null;
                Item containerItem = null;
                //check if the turret itself acts as a loader
                if (item.HasTag(Tags.TurretAmmoSource) && item.IsInteractable(character))
                {
                    if (character.AIController is HumanAIController aiController && !aiController.IgnoredItems.Contains(item)) 
                    {
                        container = item.GetComponent<ItemContainer>();
                        if (container != null) { containerItem = item; }
                    }
                }
                if (container == null)
                {
                    foreach (MapEntity e in item.linkedTo)
                    {
                        containerItem = e as Item;
                        if (containerItem == null) { continue; }
                        if (!containerItem.IsInteractable(character)) { continue; }
                        if (character.AIController is HumanAIController aiController && aiController.IgnoredItems.Contains(containerItem)) { continue; }
                        container = containerItem.GetComponent<ItemContainer>();
                        if (container != null) { break; }
                    }
                }
                if (container == null || !container.ContainableItemIdentifiers.Any())
                {
                    if (character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.GetWithVariable("DialogCannotLoadTurret", "[itemname]", item.Name, formatCapitals: FormatCapitals.Yes).Value,
                            identifier: "cannotloadturret".ToIdentifier(),
                            minDurationBetweenSimilar: 30.0f);
                    }
                    return true;
                }
                if (objective.SubObjectives.None())
                {
                    var loadItemsObjective = AIContainItems<Turret>(container, character, objective, usableProjectileCount + 1, equip: true, removeEmpty: true, dropItemOnDeselected: true,
                        spawnItemIfNotFound: objective.SpawnAmmoIfNotFound);
                    loadItemsObjective.ignoredContainerIdentifiers = ((MapEntity)containerItem).Prefab.Identifier.ToEnumerable().ToImmutableHashSet();
                    if (character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.GetWithVariable("DialogLoadTurret", "[itemname]", item.Name, formatCapitals: FormatCapitals.Yes).Value,
                            identifier: "loadturret".ToIdentifier(),
                            minDurationBetweenSimilar: 30.0f);
                    }
                    loadItemsObjective.Abandoned += CheckRemainingAmmo;
                    loadItemsObjective.Completed += CheckRemainingAmmo;
                    return false;

                    void CheckRemainingAmmo()
                    {
                        if (!character.IsOnPlayerTeam) { return; }
                        if (character.Submarine != Submarine.MainSub) { return; }
                        Identifier ammoType = container.ContainableItemIdentifiers.FirstOrNull() ?? "ammobox".ToIdentifier();
                        int remainingAmmo = Submarine.MainSub.GetItems(false).Count(i => i.HasTag(ammoType) && i.Condition > 1);
                        if (remainingAmmo == 0)
                        {
                            character.Speak(TextManager.Get($"DialogOutOf{ammoType}", "DialogOutOfTurretAmmo").Value,
                                identifier: "outofammo".ToIdentifier(),
                                minDurationBetweenSimilar: 30.0f);
                        }
                        else if (remainingAmmo < 3)
                        {
                            character.Speak(TextManager.Get($"DialogLowOn{ammoType}").Value,
                                identifier: "outofammo".ToIdentifier(),
                                minDurationBetweenSimilar: 30.0f);
                        }
                        //after the load item objective has completed (= after the bot has found ammo, either existing or magically spawned),
                        //don't allow spawning more until 60 seconds has passed
                        objective.DisableAmmoSpawnsUntil = Timing.TotalTimeUnpaused + 60.0;
                    }
                }
                if (objective.SubObjectives.Any())
                {
                    return false;
                }
            }

            //enough shells and power
            Character closestEnemy = null;
            Vector2? targetPos = null;
            float maxDistance = 10000;
            float shootDistance = AIRange * item.OffsetOnSelectedMultiplier;
            float closestDistance = maxDistance * maxDistance;
            bool hadCurrentTarget = currentTarget != null;
            if (hadCurrentTarget)
            {
                bool isValidTarget = IsValidTarget(currentTarget);
                if (isValidTarget)
                {
                    float dist = Vector2.DistanceSquared(item.WorldPosition, currentTarget.WorldPosition);
                    if (dist > closestDistance)
                    {
                        isValidTarget = false;
                    }
                    else if (currentTarget is Item targetItem)
                    {
                        if (!IsTargetItemCloseEnough(targetItem, dist))
                        {
                            isValidTarget = false;
                        }
                    }
                }
                if (!isValidTarget)
                {
                    currentTarget = null;
                    aiFindTargetTimer = CrewAIFindTargetMinInverval;
                }
            }

            if (aiFindTargetTimer <= 0.0f)
            {
                foreach (Character enemy in Character.CharacterList)
                {
                    if (enemy == character) { continue; }
                    if (!IsValidTarget(enemy)) { continue; }
                    float priority = isSlowTurret ? enemy.Params.AISlowTurretPriority : enemy.Params.AITurretPriority;
                    if (priority <= 0) { continue; }
                    //unconscious enemies are a lower prio (better to stop the ones actively attacking the sub first!)
                    if (enemy.IsUnconscious) { priority *= 0.5f; }
                    if (character.Submarine != null)
                    {
                        if (enemy.Submarine == character.Submarine) { continue; }
                        if (enemy.Submarine != null)
                        {
                            if (enemy.Submarine.Info.IsOutpost && !ShouldAttackOutpost(enemy.Submarine)) { continue; }
                            if (!IsAllowedToShootAtSub(enemy.Submarine, enemy))
                            {
                                continue;                                
                            }
                        }
                    }
                    // Don't aim monsters that are inside any submarine.
                    if (!enemy.IsHuman && enemy.CurrentHull != null) { continue; }
                    if (HumanAIController.IsFriendly(character, enemy, ignoreHuskDisguising: true)) { continue; }
                    // Don't shoot at captured enemies.
                    if (enemy.LockHands) { continue; }
                    float dist = Vector2.DistanceSquared(enemy.WorldPosition, item.WorldPosition);
                    if (dist > closestDistance) { continue; }
                    if (dist < shootDistance * shootDistance)
                    {
                        // Only check the angle to targets that are close enough to be shot at
                        // We shouldn't check the angle when a long creature is traveling outside of the shooting range, because doing so would not allow us to shoot the limbs that might be close enough to shoot at.
                        if (!IsWithinAimingRadius(enemy.WorldPosition)) { continue; }
                    }
                    if (currentTarget != null && enemy == currentTarget)
                    {
                        priority *= GetTargetPriorityModifier();
                    }
                    targetPos = enemy.WorldPosition;
                    closestEnemy = enemy;
                    closestDistance = dist / priority;
                    currentTarget = closestEnemy;
                }
                foreach (Item targetItem in Item.TurretTargetItems)
                {
                    if (!IsValidTarget(targetItem)) { continue; }
                    float priority = isSlowTurret ? targetItem.Prefab.AISlowTurretPriority : targetItem.Prefab.AITurretPriority;
                    if (priority <= 0) { continue; }
                    float dist = Vector2.DistanceSquared(item.WorldPosition, targetItem.WorldPosition);
                    if (dist > closestDistance) { continue; }
                    if (dist > shootDistance * shootDistance) { continue; }
                    if (!IsTargetItemCloseEnough(targetItem, dist)) { continue; }
                    if (!IsWithinAimingRadius(targetItem.WorldPosition)) { continue; }
                    if (currentTarget != null && targetItem == currentTarget)
                    {
                        priority *= GetTargetPriorityModifier();
                    }
                    targetPos = targetItem.WorldPosition;
                    closestDistance = dist / priority;
                    // Override the target character so that we can target the item instead.
                    closestEnemy = null;
                    currentTarget = targetItem;
                }
                aiFindTargetTimer = currentTarget == null ? CrewAiFindTargetMaxInterval : CrewAIFindTargetMinInverval;
            }
            else if (currentTarget != null)
            {
                targetPos = currentTarget.WorldPosition;
            }
            bool iceSpireSpotted = false;
            Vector2 targetVelocity = Vector2.Zero;
            // Adjust the target character position (limb or submarine)
            if (currentTarget is Character targetCharacter)
            {
                bool enemyInAnotherSub = targetCharacter.Submarine != null && targetCharacter.CurrentHull != null && targetCharacter.Submarine != item.Submarine;
                bool canSeeTarget = true;
                if (enemyInAnotherSub)
                {
                    if (lastCanSeeTargetCheck.Time < Timing.TotalTime - LineOfSightCheckInterval ||
                        targetCharacter != lastCanSeeTargetCheck.Target)
                    {
                        canSeeTarget = targetCharacter.CanSeeTarget(Item);
                        lastCanSeeTargetCheck = (targetCharacter, canSeeTarget, Timing.TotalTime);
                    }
                }

                //if the enemy is inside another sub, aim at the room they're in to make it less obvious that the enemy "knows" exactly where the target is
                if (enemyInAnotherSub && !canSeeTarget)
                {
                    //different speeds for sampling the x and y axis so we don't just go from the top-left corner of the noise map to the bottom-right but sample all over it
                    //we also swap these when sampling the y position so both the x and y don't end up with the same noise value
                    float samplingSpeed1 = 0.01f, samplingSpeed2 = 0.005f;
                    Vector2 aimNoise =
                        new Vector2(     
                            PerlinNoise.GetPerlin((float)Timing.TotalTimeUnpaused * samplingSpeed1, (float)Timing.TotalTimeUnpaused * samplingSpeed2),
                            PerlinNoise.GetPerlin((float)Timing.TotalTimeUnpaused * samplingSpeed2, (float)Timing.TotalTimeUnpaused * samplingSpeed1));
                    //convert from 0 - 1 range to -0.5 - 0.5
                    aimNoise -= new Vector2(0.5f);

                    targetPos = targetCharacter.CurrentHull.WorldPosition + targetCharacter.CurrentHull.Size * aimNoise;
                    if (closestDistance > maxDistance * maxDistance)
                    {
                        ResetTarget();
                    }
                }
                else
                {
                    // Target the closest limb. Doesn't make much difference with smaller creatures, but enables the bots to shoot longer abyss creatures like the endworm. Otherwise they just target the main body = head.
                    float closestDistSqr = closestDistance;
                    foreach (Limb limb in targetCharacter.AnimController.Limbs)
                    {
                        if (limb.IsSevered) { continue; }
                        if (limb.Hidden) { continue; }
                        if (!IsWithinAimingRadius(limb.WorldPosition)) { continue; }
                        float distSqr = Vector2.DistanceSquared(limb.WorldPosition, item.WorldPosition);
                        if (distSqr < closestDistSqr)
                        {
                            closestDistSqr = distSqr;
                            if (limb == targetCharacter.AnimController.MainLimb)
                            {
                                //prefer main limb (usually a much better target than the extremities that are often the closest limbs)
                                closestDistSqr *= 0.5f;
                            }
                            targetPos = limb.WorldPosition;
                        }
                    }
                    if (projectileSpeed < float.PositiveInfinity && targetPos.HasValue)
                    {
                        //lead the target (aim where the target will be in the future)
                        float dist = MathF.Sqrt(closestDistSqr);
                        float projectileMovementTime = dist / projectileSpeed;

                        targetVelocity = targetCharacter.AnimController.Collider.LinearVelocity;
                        Vector2 movementAmount = targetVelocity * projectileMovementTime;
                        //don't try to compensate more than 10 meters - if the target is so fast or the projectile so slow we need to go beyond that,
                        //it'd most likely fail anyway
                        movementAmount = ConvertUnits.ToDisplayUnits(movementAmount.ClampLength(MaximumAimAhead));
                        Vector2 futurePosition = targetPos.Value + movementAmount;
                        targetPos = Vector2.Lerp(targetPos.Value, futurePosition, DegreeOfSuccess(character));
                    }
                    if (closestDistSqr > shootDistance * shootDistance)
                    {
                        aiFindTargetTimer = CrewAIFindTargetMinInverval;
                        ResetTarget();
                    }
                }
                void ResetTarget()
                {
                    // Not close enough to shoot.
                    currentTarget = null;
                    closestEnemy = null;
                    targetPos = null;
                }
            }
            else if (targetPos == null && item.Submarine != null && Level.Loaded != null)
            {
                // Check ice spires
                shootDistance = AIRange * item.OffsetOnSelectedMultiplier;
                closestDistance = shootDistance;
                foreach (var wall in Level.Loaded.ExtraWalls)
                {
                    if (wall is not DestructibleLevelWall destructibleWall || destructibleWall.Destroyed) { continue; }
                    foreach (var cell in wall.Cells)
                    {
                        if (!cell.DoesDamage) { continue; }
                        foreach (var edge in cell.Edges)
                        {
                            Vector2 p1 = edge.Point1 + cell.Translation;
                            Vector2 p2 = edge.Point2 + cell.Translation;
                            Vector2 closestPoint = MathUtils.GetClosestPointOnLineSegment(p1, p2, item.WorldPosition);
                            if (!IsWithinAimingRadius(closestPoint))
                            {
                                // The closest point can't be targeted -> get a point directly in front of the turret
                                Vector2 barrelDir = new Vector2((float)Math.Cos(BarrelRotation), -(float)Math.Sin(BarrelRotation));
                                if (MathUtils.GetLineSegmentIntersection(p1, p2, item.WorldPosition, item.WorldPosition + barrelDir * shootDistance, out Vector2 intersection))
                                {
                                    closestPoint = intersection;
                                    if (!IsWithinAimingRadius(closestPoint)) { continue; }
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            float dist = Vector2.Distance(closestPoint, item.WorldPosition);

                            //add one px to make sure the visibility raycast doesn't miss the cell due to the end position being right at the edge of the cell
                            closestPoint += (closestPoint - item.WorldPosition) / Math.Max(dist, 1);

                            if (dist > AIRange + 1000) { continue; }
                            float dot = 0;
                            if (!MathUtils.NearlyEqual(item.Submarine.Velocity, Vector2.Zero))
                            {
                                dot = Vector2.Dot(Vector2.Normalize(item.Submarine.Velocity), Vector2.Normalize(closestPoint - item.Submarine.WorldPosition));
                            }
                            float minAngle = 0.5f;
                            if (dot < minAngle && dist > 1000)
                            {
                                // The sub is not moving towards the target and it's not very close to the turret either -> ignore
                                continue;
                            }
                            // Allow targeting farther when heading towards the spire (up to 1000 px)
                            dist -= MathHelper.Lerp(0, 1000, MathUtils.InverseLerp(minAngle, 1, dot));
                            if (dist > closestDistance) { continue; }
                            targetPos = closestPoint;
                            closestDistance = dist;
                            iceSpireSpotted = true;
                        }
                    }
                }
            }

            if (targetPos == null) { return false; }
            // Force the highest priority so that we don't change the objective while targeting enemies.
            objective.ForceHighestPriority = true;
#if CLIENT
            debugDrawTargetPos = targetPos.Value;
#endif
            if (closestEnemy != null && character.AIController.SelectedAiTarget != closestEnemy.AiTarget)
            {
                if (character.IsOnPlayerTeam)
                {
                    if (character.AIController.SelectedAiTarget == null && !hadCurrentTarget)
                    {
                        if (CreatureMetrics.RecentlyEncountered.Contains(closestEnemy.SpeciesName) || closestEnemy.IsHuman)
                        {
                            character.Speak(TextManager.Get("DialogNewTargetSpotted").Value,
                                identifier: "newtargetspotted".ToIdentifier(),
                                minDurationBetweenSimilar: 30.0f);
                        }
                        else if (CreatureMetrics.Encountered.Contains(closestEnemy.SpeciesName))
                        {
                            character.Speak(TextManager.GetWithVariable("DialogIdentifiedTargetSpotted", "[speciesname]", closestEnemy.DisplayName).Value,
                                identifier: "identifiedtargetspotted".ToIdentifier(),
                                minDurationBetweenSimilar: 30.0f);
                        }
                        else
                        {
                            character.Speak(TextManager.Get("DialogUnidentifiedTargetSpotted").Value,
                                identifier: "unidentifiedtargetspotted".ToIdentifier(),
                                minDurationBetweenSimilar: 5.0f);
                        }
                    }
                    else if (!closestEnemy.IsHuman && !CreatureMetrics.Encountered.Contains(closestEnemy.SpeciesName))
                    {
                        character.Speak(TextManager.Get("DialogUnidentifiedTargetSpotted").Value,
                            identifier: "unidentifiedtargetspotted".ToIdentifier(),
                            minDurationBetweenSimilar: 5.0f);
                    }
                    CreatureMetrics.AddEncounter(closestEnemy.SpeciesName);
                }
                character.AIController.SelectTarget(closestEnemy.AiTarget);
            }
            else if (iceSpireSpotted && character.IsOnPlayerTeam)
            {
                character.Speak(TextManager.Get("DialogIceSpireSpotted").Value,
                    identifier: "icespirespotted".ToIdentifier(),
                    minDurationBetweenSimilar: 60.0f);
            }
            if (character.IsBot &&
                AIMinAimDistance >= 0 &&
                Vector2.DistanceSquared(targetPos.Value, item.WorldPosition) < AIMinAimDistance* AIMinAimDistance)
            {
                character.Speak(TextManager.Get("DialogTargetTooClose").Value,
                    identifier: "targettooclose".ToIdentifier(),
                    minDurationBetweenSimilar: 30.0f);
                targetPos = item.WorldPosition + Vector2.Normalize(targetPos.Value - item.WorldPosition) * AIMinAimDistance;
                canShoot = false;
            }

            character.CursorPosition = targetPos.Value;
            if (character.Submarine != null) 
            { 
                character.CursorPosition -= character.Submarine.Position; 
            }
            
            if (IsPointingTowards(targetPos.Value))
            {
                //Use the barrel direction rather than the firing direction here.
                //If the turret has a fixed firing direction and an aimable barrel, we can assume it's something like a launcher for guided torpedoes,
                //and that the barrel direction is the one that matters for aiming purposes
                Vector2 barrelDir = GetBarrelDirection();
                Vector2 aimStartPos = GetFiringPosition();
                Vector2 aimEndPos = aimStartPos + barrelDir * shootDistance;
                bool allowShootingIfNothingInWay = false;
                if (currentTarget != null)
                {
                    Vector2 targetStartPos = currentTarget.WorldPosition;
                    Vector2 targetEndPos = currentTarget.WorldPosition + targetVelocity * ConvertUnits.ToDisplayUnits(MaximumAimAhead);

                    //if there's nothing in the way (not even the target we're trying to aim towards),
                    //shooting should only be allowed if we're aiming ahead of the target, in which case it's to be expected that we're aiming at "thin air"
                    allowShootingIfNothingInWay =
                        targetVelocity.LengthSquared() > 0.001f &&
                        MathUtils.LineSegmentsIntersect(
                           aimStartPos, aimEndPos,
                           targetStartPos, targetEndPos) &&
                        //target needs to be moving roughly perpendicular to us for aiming ahead of it to make sense
                        Math.Abs(Vector2.Dot(Vector2.Normalize(aimEndPos - aimStartPos), Vector2.Normalize(targetEndPos - targetStartPos))) < 0.5f;
                }

                if (canShoot)
                {
                    Vector2 start = ConvertUnits.ToSimUnits(aimStartPos);
                    Vector2 end = ConvertUnits.ToSimUnits(aimEndPos);
                    Body worldTarget = CheckLineOfSight(start, end);
                    if (closestEnemy is { Submarine: not null })
                    {
                        start -= closestEnemy.Submarine.SimPosition;
                        end -= closestEnemy.Submarine.SimPosition;
                        Body transformedTarget = CheckLineOfSight(start, end);
                        canShoot =
                            CanShoot(transformedTarget, allowShootingIfNothingInWay: allowShootingIfNothingInWay) &&
                            (worldTarget == null || CanShoot(worldTarget, allowShootingIfNothingInWay: allowShootingIfNothingInWay));
                    }
                    else
                    {
                        canShoot = CanShoot(worldTarget, allowShootingIfNothingInWay: allowShootingIfNothingInWay);
                    }
                }

                if (!canShoot) { return false; }
                if (character.IsOnPlayerTeam)
                {
                    character.Speak(TextManager.Get("DialogFireTurret").Value,
                        identifier: "fireturret".ToIdentifier(),
                        minDurationBetweenSimilar: 30.0f);
                }
                character.SetInput(InputType.Shoot, true, true);
            }
            return false;
        }

        private bool IsPointingTowards(Vector2 targetPos)
        {
            float enemyAngle = MathUtils.VectorToAngle(targetPos - item.WorldPosition);
            float turretAngle = -BarrelRotation;
            float maxAngleError = MathHelper.ToRadians(MaxAngleOffset);
            if (MaxChargeTime > 0.0f && currentChargingState == ChargingState.WindingUp && FiringRotationSpeedModifier > 0.0f)
            {
                //larger margin of error if the weapon needs to be charged (-> the bot can start charging when the turret is still rotating towards the target)
                maxAngleError *= 2.0f;
            }
            return Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) <= maxAngleError;
        }

        private bool IsTargetItemCloseEnough(Item target, float sqrDist) => float.IsPositiveInfinity(target.Prefab.AITurretTargetingMaxDistance) || sqrDist < MathUtils.Pow2(target.Prefab.AITurretTargetingMaxDistance);

        /// <summary>
        /// Turret doesn't consume grid power, directly takes from the batteries on its grid instead.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection conn = null)
        {
            return 0;
        }
        
        private bool IsFriendlyTeam(CharacterTeamType otherTeam)
        {
            if (User != null)
            {
                if (User.AIController is HumanAIController humanAI)
                {
                    if (humanAI.IsTeamHostile(otherTeam) ||
                        //if the target in the player theam, and the user of the turret hostile to the player team?
                        (otherTeam == CharacterTeamType.Team1 && humanAI.IsInHostileFaction()))
                    {
                        return false;
                    }
                }
                return Character.IsOnFriendlyTeam(User.TeamID, otherTeam);
            }
            return Character.IsOnFriendlyTeam(GetTurretTeam(), otherTeam);
        }

        // Not exhaustive, but helps to get rid of some code duplication
        private static bool IsValidTarget(ISpatialEntity target)
        {
            if (target == null) { return false; }
            if (target is Character targetCharacter)
            {
                if (!targetCharacter.Enabled || targetCharacter.Removed || targetCharacter.IsDead || targetCharacter.AITurretPriority <= 0)
                {
                    return false;
                }
            }
            else if (target is Item targetItem)
            {
                if (targetItem.Removed || targetItem.Condition <= 0 || !targetItem.Prefab.IsAITurretTarget || targetItem.Prefab.AITurretPriority <= 0 || targetItem.IsHidden)
                {
                    return false;
                }
                if (targetItem.Submarine != null)
                {
                    return false;
                }
                if (targetItem.ParentInventory != null)
                {
                    return false;
                }
            }
            return true;
        }

        private bool ShouldAttackOutpost(Submarine sub)
        {
            if (sub?.Info is not { IsOutpost: true }) { return false; }
            //never attack the "dummy outposts" in normal levels
            if (Level.Loaded is { Type: LevelData.LevelType.LocationConnection }) { return false; }

            //don't attack outpost belonging to no-one in particular (e.g. abandoned outposts)
            if (sub.TeamID == CharacterTeamType.None) { return false; }

            var turretTeam = GetTurretTeam();

            //this is only relevant for Team1 because we're checking for hostile factions (factions with which you have a low reputation, which only exist in the campaign)
            if (User?.AIController is HumanAIController humanAI && sub.TeamID == CharacterTeamType.Team1 &&
                (humanAI.IsInHostileFaction() || turretTeam is CharacterTeamType.Team2 or CharacterTeamType.None))
            {
                return true;
            }
            return IsFriendlyTeam(sub.TeamID);
        }

        private CharacterTeamType GetTurretTeam()
        {
            return FriendlyTeamType switch
            {
                TeamType.Team1 => CharacterTeamType.Team1,
                TeamType.Team2 => CharacterTeamType.Team2,
                TeamType.FriendlyNPC => CharacterTeamType.FriendlyNPC,
                TeamType.NoneTeam => CharacterTeamType.None,
                TeamType.OwnSub => item.Submarine?.TeamID ?? CharacterTeamType.None,
                _ => throw new NotImplementedException(),
            };
        }

        private bool IsValidTargetForAutoOperate(Character target, Identifier friendlyTag, bool ignoreTeam)
        {
            if (!friendlyTag.IsEmpty)
            {
                if (target.SpeciesName.Equals(friendlyTag) || target.Group.Equals(friendlyTag)) { return false; }
            }
            if (!ignoreTeam && IsFriendlyTeam(target.TeamID)) { return false; }
            bool isHuman = target.IsHuman || target.Group == CharacterPrefab.HumanSpeciesName;
            return isHuman ? TargetHumans : TargetMonsters;
        }

        private bool CanShoot(Body targetBody, Identifier friendlyTag = default, bool targetSubmarines = true, bool allowShootingIfNothingInWay = false, bool ignoreTeam = false)
        {
            if (targetBody == null) 
            {
                //nothing in the way (not even the target we're trying to shoot) -> no point in firing at thin air
                return allowShootingIfNothingInWay; 
            }
            Character targetCharacter = targetBody.UserData switch
            {
                Character c => c,
                Limb limb => limb.character,
                _ => null
            };
            if (targetCharacter is { Removed: false })
            {
                if (User != null)
                {
                    if (HumanAIController.IsFriendly(User, targetCharacter, ignoreHuskDisguising: true))
                    {
                        return false;
                    }
                }
                else if (!IsValidTargetForAutoOperate(targetCharacter, friendlyTag, ignoreTeam))
                {
                    // Note that Thalamus runs this even when AutoOperate is false.
                    return false;
                }
            }
            else
            {
                if (targetBody.UserData is ISpatialEntity e)
                {
                    if (e is Structure { Indestructible: true }) { return false; }
                    if (!targetSubmarines && e is Submarine) { return false; }
                    Submarine sub = e.Submarine ?? e as Submarine;
                    if (sub == null) { return true; }
                    if (sub == Item.Submarine) { return false; }
                    if (sub.Info.IsWreck || sub.Info.IsBeacon || sub.Info.IsRuin) { return false; }
                    if (sub.Info.IsOutpost && !ShouldAttackOutpost(sub)) { return false; }
                    if (!ignoreTeam && !IsAllowedToShootAtSub(sub, currentTarget as Character)) { return false; }
                }
                else if (targetBody.UserData is not Voronoi2.VoronoiCell { IsDestructible: true })
                {
                    // Hit something else, probably a level wall
                    return false;
                }
            }
            return true;
        }

        private bool IsAllowedToShootAtSub(Submarine submarine, Character targetCharacter)
        {
            if (IsFriendlyTeam(submarine.TeamID))
            {
                if (targetCharacter == null)
                {
                    //friendly sub, not trying to target a hostile inside -> no point in shooting
                    return false;
                }
                else
                {
                    //if a hostile character is operating a friendly sub (can happen e.g. when an event turns the characters in a normally-friendly sub hostile),
                    //allow damaging that friendly sub
                    bool isHostileUsingFriendlySub =
                        targetCharacter.AIController is HumanAIController humanAi &&
                        //target is in the sub we're thinking of whether to shoot at
                        targetCharacter.Submarine == submarine &&
                        //target is operating that sub
                        humanAi.ShipCommandManager is { } shipCommandManager &&
                        shipCommandManager.CommandedSubmarine == targetCharacter.Submarine;
                    bool isInFriendlyOutpost = 
                        targetCharacter.Submarine is { Info.IsOutpost: true } &&
                        GameMain.GameSession?.Campaign is { } campaign && !campaign.IsFactionHostile(targetCharacter.Submarine.Faction);
                    return isHostileUsingFriendlySub && !isInFriendlyOutpost;
                }
            }
            else
            {
                //non-friendly sub, ok to shoot at
                return true;
            }            
        }

        private Body CheckLineOfSight(Vector2 start, Vector2 end)
        {
            const Category collisionCategories = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel | Physics.CollisionProjectile;
            Body pickedBody = Submarine.PickBody(start, end, null, collisionCategories, allowInsideFixture: true,
               customPredicate: (Fixture f) =>
               {
                   //some collider belonging to the turret itself -> ignore
                   if (f.Body.UserData  == item || (f.Body.UserData is ItemComponent ic && ic.Item == item) || f.UserData == item) { return false; }
                   //on turrets and other static installations part of the sub,
                   //the userdata of the fixture is the item and the userdata of the body is the submarine
                   if ((f.UserData as Item ?? f.Body.UserData as Item) is Item otherItem) 
                   {
                       if (otherItem.GetComponent<Turret>() != null) { return false; }
                       //ignore projectiles fired by this turret - we don't want line of sight to be blocked by our own projectiles when firing some weapon with a high rate of fire
                       if (otherItem.GetComponent<Projectile>() is Projectile otherProjectile && otherProjectile.Launcher == item) { return false; }
                   }
                   if (f.CollidesWith == Physics.CollisionNone) { return false; }
                   if (f.UserData is Hull) { return false; }
                   return !item.StaticFixtures.Contains(f);
               });
            return pickedBody;
        }

        public Vector2 GetBarrelFiringPosition(bool useOffset = true)
        {
            Vector2 transformedFiringOffset = Vector2.Zero;
            if (useOffset)
            {
                Vector2 currOffSet = FiringOffset;
                if (flipFiringOffset) { currOffSet.X = -currOffSet.X; }
                transformedFiringOffset = MathUtils.RotatePoint(new Vector2(-currOffSet.Y, -currOffSet.X) * item.Scale, -BarrelRotation);
            }
            return new Vector2(item.WorldRect.X + transformedBarrelPos.X + transformedFiringOffset.X, item.WorldRect.Y - transformedBarrelPos.Y + transformedFiringOffset.Y);
        }
        
        public Vector2 GetFixedFiringPosition()
        {
            return new Vector2(item.WorldRect.X + transformedFixedFiringPos.X, item.WorldRect.Y - transformedFixedFiringPos.Y);
        }

        /// <summary>
        /// Returns the firing position (where the projectiles spawn, usually the end of the barrel) in world coordinates.
        /// </summary>
        public Vector2 GetFiringPosition() => useFixedFiringPos ? GetFixedFiringPosition() : GetBarrelFiringPosition();
        
        private float CalculateChargeRotation(float targetRot, float chargeRatio)
        {
            float rotation = FiringRotation + MathHelper.PiOver2;
            if (targetRot != 0)
            {
                rotation = MathHelper.Lerp(rotation, rotation + targetRot, chargeRatio);
            }
            return rotation;
        }
        
        private Vector2 CalculateChargePositionOffset(Vector2 offset, float rotation, float chargeRatio) => MathUtils.RotatePoint(new Vector2(offset.X * chargeRatio, offset.Y * chargeRatio) * item.Scale, rotation);

        private bool IsWithinAimingRadius(float angle)
        {
            float midRotation = (minRotation + maxRotation) / 2.0f;
            while (midRotation - angle < -MathHelper.Pi) { angle -= MathHelper.TwoPi; }
            while (midRotation - angle > MathHelper.Pi) { angle += MathHelper.TwoPi; }
            return angle >= minRotation && angle <= maxRotation;
        }

        public bool IsWithinAimingRadius(Vector2 target) => IsWithinAimingRadius(-MathUtils.VectorToAngle(target - item.WorldPosition));

        /// <summary>
        /// Check whether any point of a target of a given size is within the aiming radius of the turret.
        /// </summary>
        public bool IsWithinAimingRadius(Vector2 target, Vector2 targetSize)
        {
            Vector2 origin = GetBarrelFiringPosition();
            //simpler case: fixed firing angle, we can just check for a line-rectangle intersection
            if (MathUtils.NearlyEqual(minRotation, maxRotation) || useFixedFiringPos)
            {
                //we don't really care about the range here, let's just use a very large value for the calculation
                float range = 10000.0f;
                return MathUtils.GetLineRectangleIntersection(
                    origin, origin + GetFiringDirection() * range,
                    new Rectangle((target - targetSize).ToPoint(), targetSize.ToPoint()), out _);
            }

            float minAngle = float.PositiveInfinity;
            float maxAngle = float.NegativeInfinity;

            Vector2 halfSize = new Vector2(targetSize.X, targetSize.Y) / 2.0f;

            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = i switch
                {
                    0 => target - halfSize,
                    1 => target + new Vector2(-halfSize.X, halfSize.Y),
                    2 => target + halfSize,
                    3 => target + new Vector2(halfSize.X, -halfSize.Y),
                    _ => throw new InvalidOperationException()
                };

                float angle = -MathUtils.VectorToAngle(corner - origin);

                // Normalize angles relative to current aiming center
                float midRotation = (minRotation + maxRotation) * 0.5f;
                while (angle - midRotation < -MathHelper.Pi) { angle += MathHelper.TwoPi; }
                while (angle - midRotation > MathHelper.Pi) { angle -= MathHelper.TwoPi; }

                minAngle = Math.Min(minAngle, angle);
                maxAngle = Math.Max(maxAngle, angle);
            }
            // Rectangle angular span intersects aiming radius?
            return maxAngle >= minRotation && minAngle <= maxRotation;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            barrelSprite?.Remove(); barrelSprite = null;
            railSprite?.Remove(); railSprite = null;
            barrelSpriteBroken?.Remove(); barrelSpriteBroken = null;
            railSpriteBroken?.Remove(); railSpriteBroken = null;

#if CLIENT
            crosshairSprite?.Remove(); crosshairSprite = null;
            crosshairPointerSprite?.Remove(); crosshairPointerSprite = null;
            moveSoundChannel?.Dispose(); moveSoundChannel = null;
            WeaponIndicatorSprite?.Remove(); WeaponIndicatorSprite = null;
            if (powerIndicator != null)
            {
                powerIndicator.RectTransform.Parent = null;
                powerIndicator = null;
            }
#endif
            projectiles.Clear();
        }

        public IEnumerable<Item> GetLinkedLoaders()
        {
            for (int j = 0; j < item.linkedTo.Count; j++)
            {
                var e = item.linkedTo[(j + currentLoaderIndex) % item.linkedTo.Count];
                if (!item.Prefab.IsLinkAllowed(e.Prefab)) { continue; }
                if (e is Item projectileContainer)
                {
                    yield return projectileContainer;
                }
            }
        }

        private readonly static List<Projectile> projectiles = new List<Projectile>();
        public IEnumerable<Projectile> GetLoadedProjectiles()
        {
            projectiles.Clear();
            // check the item itself first
            CheckProjectileContainer(item, projectiles, out bool _);
            foreach (Item projectileContainer in GetLinkedLoaders())
            {
                CheckProjectileContainer(projectileContainer, projectiles, out bool stopSearching);
                if (projectiles.Any() || stopSearching) { return projectiles; }                
            }
            return projectiles;
        }

        /// <summary>
        /// Returns the <see cref="TurretAmmo"/> component of the ammunition the turret would fire next, or null if there's none (or the ammo doesn't modify the turret's stats).
        /// </summary>
        public TurretAmmo GetActiveTurretAmmo() => GetActiveAmmo()?.GetComponent<TurretAmmo>();

        /// <summary>
        /// The <see cref="TurretAmmo"/> of the most recent shot. Use this for things that react to the shot (launch particles, an ElectricalDischarger triggered by the shot),
        /// because by then the ammo box may already be empty and no longer count as the active ammo.
        /// </summary>
        public TurretAmmo LastLaunchedTurretAmmo { get; private set; }

        /// <summary>
        /// Finds the item in the turret or the linked loaders that the turret would fire (or use to spawn the projectile, e.g. a pulse laser ammo box).
        /// Uses the same search order as <see cref="GetLoadedProjectiles"/>.
        /// </summary>
        private Item GetActiveAmmo()
        {
            //the turret itself only counts if there's something that can actually be fired inside it (GetLoadedProjectiles doesn't stop searching at the turret either)
            if (FindActiveAmmo(item, requireProjectiles: true) is Item ownAmmo) { return ownAmmo; }
            foreach (Item projectileContainer in GetLinkedLoaders())
            {
                if (FindActiveAmmo(projectileContainer, requireProjectiles: false) is Item ammo) { return ammo; }
            }
            return null;

            static Item FindActiveAmmo(Item projectileContainer, bool requireProjectiles)
            {
                if (projectileContainer.Condition <= 0.0f) { return null; }
                foreach (Item containedItem in projectileContainer.ContainedItems)
                {
                    //same criteria as in CheckProjectileContainer: a projectile, or a container that has projectiles inside it or still has ammo left
                    if (containedItem.GetComponent<Projectile>() != null ||
                        containedItem.ContainedItems.Any(static it => it.GetComponent<Projectile>() != null) ||
                        (!requireProjectiles && containedItem.Condition > 0.0f))
                    {
                        return containedItem;
                    }
                }
                return null;
            }
        }

        private static void CheckProjectileContainer(Item projectileContainer, List<Projectile> projectiles, out bool stopSearching)
        {
            stopSearching = false;
            if (projectileContainer.Condition <= 0.0f) { return; }

            var containedItems = projectileContainer.ContainedItems;
            if (containedItems == null) { return; }

            foreach (Item containedItem in containedItems)
            {
                var projectileComponent = containedItem.GetComponent<Projectile>();
                if (projectileComponent != null && projectileComponent.Item.body != null)
                {
                    projectiles.Add(projectileComponent);
                    return;
                }
                else
                {
                    //check if the contained item is another itemcontainer with projectiles inside it
                    foreach (Item subContainedItem in containedItem.ContainedItems)
                    {
                        projectileComponent = subContainedItem.GetComponent<Projectile>();
                        if (projectileComponent != null && projectileComponent.Item.body != null)
                        {
                            projectiles.Add(projectileComponent);
                        }
                    }
                    // in the case that we found a container that still has condition/ammo left,
                    // return and inform GetLoadedProjectiles to stop searching past this point (even if no projectiles were not found)
                    if (containedItem.Condition > 0.0f || projectiles.Any())
                    {
                        stopSearching = true;
                        return;
                    }
                }
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            //if rotation from 0 to 360 deg is allowed, we don't need to do anything to the rotation limits
            bool unlimitedRotation =
                MathUtils.NearlyEqual(minRotation, 0.0f) &&
                MathUtils.NearlyEqual(maxRotation, MathHelper.TwoPi);

            if (!unlimitedRotation)
            {
                minRotation = MathHelper.Pi - minRotation;
                maxRotation = MathHelper.Pi - maxRotation;

                (minRotation, maxRotation) = (maxRotation, minRotation);

                barrelPos.X = item.Rect.Width / item.Scale - barrelPos.X;

                while (minRotation < 0)
                {
                    minRotation += MathHelper.TwoPi;
                    maxRotation += MathHelper.TwoPi;
                }
                targetRotation = BarrelRotation = (minRotation + maxRotation) / 2;
            }

            fixedFiringPos.X = item.Rect.Width / item.Scale - barrelPos.X;

            UpdateTransformedBarrelPos();
            UpdateLightComponents();
        }

        public override void FlipY(bool relativeToSub)
        {
            BaseRotation = MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(MathHelper.ToRadians(180 - BaseRotation)));

            //if rotation from 0 to 360 deg is allowed, we don't need to do anything to the rotation limits
            bool unlimitedRotation =
                MathUtils.NearlyEqual(minRotation, 0.0f) &&
                MathUtils.NearlyEqual(maxRotation, MathHelper.TwoPi);

            if (!unlimitedRotation)
            {
                minRotation = -minRotation;
                maxRotation = -maxRotation;

                (minRotation, maxRotation) = (maxRotation, minRotation);

                while (minRotation < 0)
                {
                    minRotation += MathHelper.TwoPi;
                    maxRotation += MathHelper.TwoPi;
                }
                targetRotation = BarrelRotation = (minRotation + maxRotation) / 2;
            }

            UpdateTransformedBarrelPos();
            UpdateLightComponents();
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            Character sender = signal.sender;
            switch (connection.Name)
            {
                case "position_in":
                    if (float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newRotation))
                    {
                        if (!MathUtils.IsValid(newRotation)) { return; }
                        targetRotation = MathHelper.ToRadians(newRotation);
                        IsActive = true;
                        receivedPositionSignal = true;
                    }
                    if (User != null && sender == null)
                    {
                        //reset active user immediately if we receive a signal
                        //not sent by a character (which happens when a user deselects a periscope)
                        resetActiveUserTimer = 0.0f;
                    }
                    User = sender;
                    break;
                case "trigger_in":
                    if (signal.value == "0") { return; }
                    item.Use((float)Timing.Step, user: sender);
                    User = sender;
                    //triggering the Use method through item.Use will fail if the item is not characterusable and the signal was sent by a character
                    //so lets do it manually
                    if (!characterUsable && sender != null)
                    {
                        TryLaunch((float)Timing.Step, sender);
                    }
                    break;
                case "toggle_light":
                    if (lightComponents != null && signal.value != "0")
                    {
                        foreach (var light in lightComponents)
                        {
                            light.IsOn = !light.IsOn;
                        }
                        UpdateLightComponents();
                    }
                    break;
                case "set_light":
                    if (lightComponents != null)
                    {
                        bool shouldBeOn = signal.value != "0";
                        foreach (var light in lightComponents)
                        {
                            light.IsOn = shouldBeOn;
                        }
                        UpdateLightComponents();
                    }
                    break;
                case SetAutoOperateConnection:
                    if (!AllowAutoOperateWithWiring) { return; }
                    AutoOperate = signal.value != "0";
                    break;
                case ToggleAutoOperateConnection:
                    if (!AllowAutoOperateWithWiring) { return; }
                    if (signal.value != "0")
                    {
                        AutoOperate = !AutoOperate;
                    }
                    break;
            }
        }

        private Vector2? loadedRotationLimits;
        private float? loadedBaseRotation;
        private TeamType? loadedFriendlyTeamType;

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            loadedRotationLimits = componentElement.GetAttributeVector2("rotationlimits", RotationLimits);
            loadedBaseRotation = componentElement.GetAttributeFloat("baserotation", componentElement.Parent.GetAttributeFloat("rotation", BaseRotation));

            //backwards compatibility: previously None made the turret consider the team the submarine/outpost belongs as the friendly team
            if (componentElement.GetAttribute("FriendlyTeam") is { } friendlyTeamAttribute)
            {
                CharacterTeamType friendlyTeam = XMLExtensions.ParseEnumValue(friendlyTeamAttribute.Value, defaultValue: CharacterTeamType.None, friendlyTeamAttribute);
                loadedFriendlyTeamType = friendlyTeam switch
                {
                    CharacterTeamType.None => TeamType.OwnSub,
                    CharacterTeamType.Team1 => TeamType.Team1,
                    CharacterTeamType.Team2 => TeamType.Team2,
                    CharacterTeamType.FriendlyNPC => TeamType.FriendlyNPC,
                    _ => throw new NotImplementedException()
                };
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            FindLightComponents();
            targetRotation = BarrelRotation;
            if (!loadedBaseRotation.HasValue)
            {
                if (item.FlippedX) { FlipX(relativeToSub: false); }
                if (item.FlippedY) { FlipY(relativeToSub: false); }
            }
            UpdateTransformedBarrelPos();
            UpdateLightComponents();
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            if (TryExtractEventData(extraData, out EventData eventData))
            {
                msg.WriteUInt16(eventData.Projectile?.ID ?? LaunchWithoutProjectileId);
                msg.WriteRangedSingle(MathHelper.Clamp(wrapAngle(FiringRotation), minRotation, maxRotation), minRotation, maxRotation, 16);
            }
            else
            {
                msg.WriteUInt16((ushort)0);
                msg.WriteRangedSingle(MathHelper.Clamp(wrapAngle(targetRotation), minRotation, maxRotation), minRotation, maxRotation, 16);
            }

            float wrapAngle(float angle)
            {
                float wrappedAngle = angle;
                while (wrappedAngle < minRotation && MathUtils.IsValid(wrappedAngle))
                {
                    wrappedAngle += MathHelper.TwoPi;
                }
                while (wrappedAngle > maxRotation && MathUtils.IsValid(wrappedAngle))
                {
                    wrappedAngle -= MathHelper.TwoPi;
                }
                return wrappedAngle;
            }
        }
    }
}


