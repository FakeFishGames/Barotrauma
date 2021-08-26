using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using FarseerPhysics.Dynamics;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite barrelSprite, railSprite;
        private readonly List<(Sprite sprite, Vector2 position)> chargeSprites = new List<(Sprite sprite, Vector2 position)>();
        private readonly List<Sprite> spinningBarrelSprites = new List<Sprite>();

        private Vector2 barrelPos;
        private Vector2 transformedBarrelPos;

        private LightComponent lightComponent;
        
        private float rotation, targetRotation;

        private float reload, reloadTime;

        private float minRotation, maxRotation;

        private float launchImpulse;

        private Camera cam;

        private float angularVelocity;

        private int failedLaunchAttempts;

        private float currentChargeTime;
        private bool tryingToCharge;

        private enum ChargingState
        {
            Inactive,
            WindingUp,
            WindingDown,
        }

        private ChargingState currentChargingState;

        private float currentBarrelSpin = 0f;

        private readonly List<Item> activeProjectiles = new List<Item>();
        public IEnumerable<Item> ActiveProjectiles => activeProjectiles;

        private Character user;

        private float resetUserTimer;

        private float aiTargetingGraceTimer;

        private float aiFindTargetTimer;
        private Character currentTarget; 
        const float aiFindTargetInterval = 5.0f;

        public float Rotation
        {
            get { return rotation; }
        }
        
        [Serialize("0,0", false, description: "The position of the barrel relative to the upper left corner of the base sprite (in pixels).")]
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

        [Serialize("0,0", false, description: "The projectile launching location relative to transformed barrel position (in pixels).")]
        public Vector2 FiringOffset
        {
            get;
            set;
        }
        public Vector2 TransformedBarrelPos
        {
            get
            {
                return transformedBarrelPos;
            }
        }

        [Serialize(0.0f, false, description: "The impulse applied to the physics body of the projectile (the higher the impulse, the faster the projectiles are launched).")]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [Editable(0.0f, 1000.0f, decimals: 3), Serialize(5.0f, false, description: "The period of time the user has to wait between shots.")]
        public float Reload
        {
            get { return reloadTime; }
            set { reloadTime = value; }
        }

        [Editable(0.1f, 10f), Serialize(1.0f, false, description: "Modifies the duration of retraction of the barrell after recoil to get back to the original position after shooting. Reload time affects this too.")]
        public float RetractionDurationMultiplier
        {
            get;
            set;
        }

        [Editable(0.1f, 10f), Serialize(0.1f, false, description: "How quickly the recoil moves the barrel after launching.")]
        public float RecoilTime
        {
            get;
            set;
        }

        [Editable(0f, 1000f), Serialize(0f, false, description: "How long the barrell stays in place after the recoil and before retracting back to the original position.")]
        public float RetractionDelay
        {
            get;
            set;
        }

        [Serialize(1, false, description: "How many projectiles the weapon launches when fired once.")]
        public int ProjectileCount
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the turret be fired without projectiles (causing it just to execute the OnUse effects and the firing animation without actually firing anything).")]
        public bool LaunchWithoutProjectile
        {
            get;
            set;
        }

        [Editable(VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" }), 
            Serialize("0.0,0.0", true, description: "The range at which the barrel can rotate.", alwaysUseInstanceValues: true)]
        public Vector2 RotationLimits
        {
            get
            {
                return new Vector2(MathHelper.ToDegrees(minRotation), MathHelper.ToDegrees(maxRotation)); 
            }
            set
            {
                minRotation = MathHelper.ToRadians(Math.Min(value.X, value.Y));
                maxRotation = MathHelper.ToRadians(Math.Max(value.X, value.Y));

                rotation = (minRotation + maxRotation) / 2;
#if CLIENT
                if (lightComponent != null) 
                {
                    lightComponent.Rotation = rotation;
                    lightComponent.Light.Rotation = -rotation;
                }
#endif
            }
        }

        [Serialize(0.0f, false, description: "Random spread applied to the firing angle of the projectiles (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(5.0f, false, description: "How much torque is applied to rotate the barrel when the item is used by a character"
            + " with insufficient skills to operate it. Higher values make the barrel rotate faster.")]
        public float SpringStiffnessLowSkill
        {
            get;
            private set;
        }
        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(2.0f, false, description: "How much torque is applied to rotate the barrel when the item is used by a character"
            + " with sufficient skills to operate it. Higher values make the barrel rotate faster.")]
        public float SpringStiffnessHighSkill
        {
            get;
            private set;
        }

        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(50.0f, false, description: "How much torque is applied to resist the movement of the barrel when the item is used by a character"
            + " with insufficient skills to operate it. Higher values make the aiming more \"snappy\", stopping the barrel from swinging around the direction it's being aimed at.")]
        public float SpringDampingLowSkill
        {
            get;
            private set;
        }
        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(10.0f, false, description: "How much torque is applied to resist the movement of the barrel when the item is used by a character"
            + " with sufficient skills to operate it. Higher values make the aiming more \"snappy\", stopping the barrel from swinging around the direction it's being aimed at.")]
        public float SpringDampingHighSkill
        {
            get;
            private set;
        }

        [Editable(0.0f, 100.0f, DecimalCount = 2),
            Serialize(1.0f, false, description: "Maximum angular velocity of the barrel when used by a character with insufficient skills to operate it.")]
        public float RotationSpeedLowSkill
        {
            get;
            private set;
        }
        [Editable(0.0f, 100.0f, DecimalCount = 2),
            Serialize(5.0f, false, description: "Maximum angular velocity of the barrel when used by a character with sufficient skills to operate it."),]
        public float RotationSpeedHighSkill
        {
            get;
            private set;
        }

        [Serialize(1.0f, false, description: "How fast the turret can rotate while firing (for charged weapons).")]
        public float FiringRotationSpeedModifier
        {
            get;
            private set;
        }

        [Serialize(false, true, description: "Whether the turret should always charge-up fully to shoot.")]
        public bool SingleChargedShot
        {
            get;
            private set;
        }

        private float prevScale;
        float prevBaseRotation;
        [Serialize(0.0f, true, description: "The angle of the turret's base in degrees.", alwaysUseInstanceValues: true)]
        public float BaseRotation
        {
            get { return item.Rotation; }
            set
            {
                item.Rotation = value;
                UpdateTransformedBarrelPos();
            }
        }

        [Serialize(3000.0f, true, description: "How close to a target the turret has to be for an AI character to fire it.")]
        public float AIRange
        {
            get;
            set;
        }

        [Serialize(-1, true, description: "The turret won't fire additional projectiles if the number of previously fired, still active projectiles reaches this limit. If set to -1, there is no limit to the number of projectiles.")]
        public int MaxActiveProjectiles
        {
            get;
            set;
        }

        [Serialize(0f, true, description: "The time required for a charge-type turret to charge up before able to fire.")]
        public float MaxChargeTime
        {
            get;
            private set;
        }

        public Turret(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "barrelsprite":
                        barrelSprite = new Sprite(subElement);
                        break;
                    case "railsprite":
                        railSprite = new Sprite(subElement);
                        break;
                    case "chargesprite":
                        chargeSprites.Add((new Sprite(subElement), subElement.GetAttributeVector2("chargetarget", Vector2.Zero)));
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
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        private void UpdateTransformedBarrelPos()
        {
            transformedBarrelPos = MathUtils.RotatePointAroundTarget(barrelPos * item.Scale, new Vector2(item.Rect.Width / 2, item.Rect.Height / 2), MathHelper.ToRadians(item.Rotation));
#if CLIENT
            item.ResetCachedVisibleSize();
#endif
            prevBaseRotation = item.Rotation;
            prevScale = item.Scale;
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            FindLightComponent();
            if (loadedRotationLimits.HasValue) { RotationLimits = loadedRotationLimits.Value; }
            if (loadedBaseRotation.HasValue) { BaseRotation = loadedBaseRotation.Value; }
            UpdateTransformedBarrelPos();
        }

        private void FindLightComponent()
        {
            foreach (LightComponent lc in item.GetComponents<LightComponent>())
            {
                if (lc?.Parent == this)
                {
                    lightComponent = lc;
                    break;
                }
            }

#if CLIENT
            if (lightComponent != null) 
            {
                lightComponent.Parent = null;
                lightComponent.Rotation = Rotation - MathHelper.ToRadians(item.Rotation);
                lightComponent.Light.Rotation = -rotation;
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

            if (user != null && user.Removed)
            {
                user = null;
            }
            else
            {
                resetUserTimer -= deltaTime;
                if (resetUserTimer <= 0.0f) { user = null; }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            float previousChargeTime = currentChargeTime;

            if (SingleChargedShot && reload > 0f)
            {
                // single charged shot guns will decharge after firing
                // for cosmetic reasons, this is done by lerping in half the reload time
                currentChargeTime = Math.Max(0f, MaxChargeTime * (reload / reloadTime - 0.5f));
            }
            else
            {
                float chargeDeltaTime = tryingToCharge ? deltaTime : -deltaTime;
                currentChargeTime = Math.Clamp(currentChargeTime + chargeDeltaTime, 0f, MaxChargeTime);
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
            }

            UpdateProjSpecific(deltaTime);

            if (MathUtils.NearlyEqual(minRotation, maxRotation))
            {
                UpdateLightComponent();
                return;
            }

            float targetMidDiff = MathHelper.WrapAngle(targetRotation - (minRotation + maxRotation) / 2.0f);

            float maxDist = (maxRotation - minRotation) / 2.0f;

            if (Math.Abs(targetMidDiff) > maxDist)
            {
                targetRotation = (targetMidDiff < 0.0f) ? minRotation : maxRotation;
            }

            float degreeOfSuccess = user == null ? 0.5f : DegreeOfSuccess(user);
            if (degreeOfSuccess < 0.5f) { degreeOfSuccess *= degreeOfSuccess; } //the ease of aiming drops quickly with insufficient skill levels
            float springStiffness = MathHelper.Lerp(SpringStiffnessLowSkill, SpringStiffnessHighSkill, degreeOfSuccess);
            float springDamping = MathHelper.Lerp(SpringDampingLowSkill, SpringDampingHighSkill, degreeOfSuccess);
            float rotationSpeed = MathHelper.Lerp(RotationSpeedLowSkill, RotationSpeedHighSkill, degreeOfSuccess);
            if (MaxChargeTime > 0)
            {
                rotationSpeed *= MathHelper.Lerp(1f, FiringRotationSpeedModifier, MathUtils.EaseIn(currentChargeTime / MaxChargeTime));
            }

            // Do not increase the weapons skill when operating a turret in an outpost level
            if (user?.Info != null && (GameMain.GameSession?.Campaign == null || !Level.IsLoadedOutpost))
            {
                user.Info.IncreaseSkillLevel("weapons",
                    SkillSettings.Current.SkillIncreasePerSecondWhenOperatingTurret * deltaTime / Math.Max(user.GetSkillLevel("weapons"), 1.0f),
                    user.Position + Vector2.UnitY * 150.0f);
            }

            float rotMidDiff = MathHelper.WrapAngle(rotation - (minRotation + maxRotation) / 2.0f);

            float targetRotationDiff = MathHelper.WrapAngle(targetRotation - rotation);

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

            rotation += angularVelocity * deltaTime;

            rotMidDiff = MathHelper.WrapAngle(rotation - (minRotation + maxRotation) / 2.0f);

            if (rotMidDiff < -maxDist)
            {
                rotation = minRotation;
                angularVelocity *= -0.5f;
            }
            else if (rotMidDiff > maxDist)
            {
                rotation = maxRotation;
                angularVelocity *= -0.5f;
            }

            if (aiTargetingGraceTimer > 0f)
            {
                aiTargetingGraceTimer -= deltaTime;
            }
            if (aiFindTargetTimer > 0.0f)
            {
                aiFindTargetTimer -= deltaTime;
            }

            UpdateLightComponent();
        }

        private void UpdateLightComponent()
        {
            if (lightComponent != null)
            {
                lightComponent.Rotation = Rotation - MathHelper.ToRadians(item.Rotation);
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!characterUsable && character != null) { return false; }
            return TryLaunch(deltaTime, character);
        }

        public bool HasPowerToShoot()
        {
            return GetAvailableBatteryPower() >= powerConsumption;
        }

        private bool TryLaunch(float deltaTime, Character character = null, bool ignorePower = false)
        {
            tryingToCharge = true;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }

            if (currentChargeTime < MaxChargeTime) { return false; }

            if (reload > 0.0f) { return false; }

            if (MaxActiveProjectiles >= 0)
            {
                activeProjectiles.RemoveAll(it => it.Removed);
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
                    return false;
                }
            }

            Projectile launchedProjectile = null;
            bool loaderBroken = false;
            bool isTinkering = false;
            for (int i = 0; i < ProjectileCount; i++)
            {
                var projectiles = GetLoadedProjectiles();
                if (projectiles.Any())
                {
                    ItemContainer projectileContainer = projectiles.First().Item.Container?.GetComponent<ItemContainer>();
                    if (projectileContainer != null && projectileContainer.Item != item) 
                    { 
                        projectileContainer?.Item.Use(deltaTime, null); 
                    }
                }
                else
                {
                    foreach (MapEntity e in item.linkedTo)
                    {
                        //use linked projectile containers in case they have to react to the turret being launched somehow
                        //(play a sound, spawn more projectiles)
                        if (!(e is Item linkedItem)) { continue; }
                        if (!item.prefab.IsLinkAllowed(e.prefab)) { continue; }
                        if (linkedItem.Condition <= 0.0f) 
                        {
                            loaderBroken = true;
                            continue; 
                        }
                        ItemContainer projectileContainer = linkedItem.GetComponent<ItemContainer>();
                        if (projectileContainer != null)
                        {
                            linkedItem.Use(deltaTime, null);
                            projectiles = GetLoadedProjectiles();
                            if (projectiles.Any()) { break; }
                        }

                    }
                }
                if (projectiles.Count == 0 && !LaunchWithoutProjectile)
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

                foreach (MapEntity e in item.linkedTo)
                {
                    if (!(e is Item linkedItem)) { continue; }
                    if (!item.prefab.IsLinkAllowed(e.prefab)) { continue; }
                    if (linkedItem.GetComponent<Repairable>() is Repairable repairable && linkedItem.HasTag("turretammosource"))
                    {
                        isTinkering = repairable.IsTinkering;
                    }
                }

                if (!ignorePower)
                {
                    var batteries = item.GetConnectedComponents<PowerContainer>();
                    float neededPower = powerConsumption;
                    if (isTinkering)
                    {
                        neededPower /= 1.25f;
                    }
                    while (neededPower > 0.0001f && batteries.Count > 0)
                    {
                        batteries.RemoveAll(b => b.Charge <= 0.0001f || b.MaxOutPut <= 0.0001f);
                        float takePower = neededPower / batteries.Count;
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
                    if (projectiles.Any())
                    {
                        foreach (Projectile projectile in projectiles)
                        {
                            Launch(projectile.Item, character, isTinkering: isTinkering);
                        }
                    }
                    else
                    {
                        Launch(null, character, isTinkering: isTinkering);
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
                }
            }

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

        private void Launch(Item projectile, Character user = null, float? launchRotation = null, bool isTinkering = false)
        {
            reload = reloadTime;
            if (isTinkering) 
            {
                reload /= 1.25f;
            }

            if (user != null)
            {
                reload /= 1 + user.GetStatValue(StatTypes.TurretAttackSpeed);
            }

            if (projectile != null)
            {
                activeProjectiles.Add(projectile);
                projectile.Drop(null);
                if (projectile.body != null) 
                {                 
                    projectile.body.Dir = 1.0f;
                    projectile.body.ResetDynamics();
                    projectile.body.Enabled = true;
                }
                
                float spread = MathHelper.ToRadians(Spread) * Rand.Range(-0.5f, 0.5f);
                projectile.SetTransform(
                    ConvertUnits.ToSimUnits(GetRelativeFiringPosition()), 
                    -(launchRotation ?? rotation) + spread);
                projectile.UpdateTransform();
                projectile.Submarine = projectile.body?.Submarine;

                Projectile projectileComponent = projectile.GetComponent<Projectile>();
                if (projectileComponent != null)
                {
                    projectileComponent.Attacker = user;
                    if (isTinkering)
                    {
                        projectileComponent.Attack.DamageMultiplier *= 1.25f;
                    }
                    projectileComponent.Use();
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

                if (projectile.Container != null) { projectile.Container.RemoveContained(projectile); }            
            }
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), projectile });
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f, user: user);
            LaunchProjSpecific();
        }

        partial void LaunchProjSpecific();

        private void ShiftItemsInProjectileContainer(ItemContainer container)
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
        private float disorderTimer;

        private float prevTargetRotation;
        private float updateTimer;
        private bool updatePending;
        public void ThalamusOperate(WreckAI ai, float deltaTime, bool targetHumans, bool targetOtherCreatures, bool targetSubmarines, bool ignoreDelay)
        {
            if (ai == null) { return; }

            IsActive = true;

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
                }
                updateTimer -= deltaTime;
            }

            if (!ignoreDelay && waitTimer > 0)
            {
                waitTimer -= deltaTime;
                return;
            }
            Submarine closestSub = null;
            float maxDistance = 10000.0f;
            float shootDistance = AIRange;
            ISpatialEntity target = null;
            float closestDist = shootDistance * shootDistance;
            if (targetHumans || targetOtherCreatures)
            {
                foreach (var character in Character.CharacterList)
                {
                    if (character == null || character.Removed || character.IsDead) { continue; }
                    if (character.Params.Group.Equals(ai.Config.Entity, StringComparison.OrdinalIgnoreCase)) { continue; }
                    bool isHuman = character.IsHuman || character.Params.Group.Equals(CharacterPrefab.HumanSpeciesName, StringComparison.OrdinalIgnoreCase);
                    if (isHuman)
                    {
                        if (!targetHumans)
                        {
                            // Don't target humans if not defined to.
                            continue;
                        }
                    }
                    else if (!targetOtherCreatures)
                    {
                        // Don't target other creatures if not defined to.
                        continue;
                    }
                    float dist = Vector2.DistanceSquared(character.WorldPosition, item.WorldPosition);
                    if (dist > closestDist) { continue; }
                    target = character;
                    closestDist = dist;
                }
            }
            if (targetSubmarines)
            {
                if (target == null || target.Submarine != null)
                {
                    closestDist = maxDistance * maxDistance;
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub.Info.Type != SubmarineType.Player) { continue; }
                        float dist = Vector2.DistanceSquared(sub.WorldPosition, item.WorldPosition);
                        if (dist > closestDist) { continue; }
                        closestSub = sub;
                        closestDist = dist;
                    }
                    closestDist = shootDistance * shootDistance;
                    if (closestSub != null)
                    {
                        foreach (var hull in Hull.hullList)
                        {
                            if (!closestSub.IsEntityFoundOnThisSub(hull, true)) { continue; }
                            float dist = Vector2.DistanceSquared(hull.WorldPosition, item.WorldPosition);
                            if (dist > closestDist) { continue; }
                            target = hull;
                            closestDist = dist;
                        }
                    }
                }
            }
            if (!ignoreDelay)
            {
                if (target == null)
                {
                    // Random movement
                    waitTimer = Rand.Value(Rand.RandSync.Unsynced) < 0.98f ? 0f : Rand.Range(5f, 20f);
                    targetRotation = Rand.Range(minRotation, maxRotation);
                    updatePending = true;
                    return;
                }
                if (disorderTimer < 0)
                {
                    // Random disorder
                    disorderTimer = Rand.Range(0f, 3f);
                    waitTimer = Rand.Range(0.25f, 1f);
                    targetRotation = MathUtils.WrapAngleTwoPi(targetRotation += Rand.Range(-1f, 1f));
                    updatePending = true;
                    return;
                }
                else
                {
                    disorderTimer -= deltaTime;
                }
            }
            if (target == null) { return; }
      
            float angle = -MathUtils.VectorToAngle(target.WorldPosition - item.WorldPosition);
            targetRotation = MathUtils.WrapAngleTwoPi(angle);

            if (Math.Abs(targetRotation - prevTargetRotation) > 0.1f) { updatePending = true; }

            if (target is Hull targetHull)
            {
                Vector2 barrelDir = new Vector2((float)Math.Cos(rotation), -(float)Math.Sin(rotation));
                if (!MathUtils.GetLineRectangleIntersection(item.WorldPosition, item.WorldPosition + barrelDir * AIRange, targetHull.WorldRect, out _))
                {
                    return;
                }
            }
            else
            {
                if (!CheckTurretAngle(angle)) { return; }
                float enemyAngle = MathUtils.VectorToAngle(target.WorldPosition - item.WorldPosition);
                float turretAngle = -rotation;
                if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.15f) { return; }
            }
            Vector2 start = ConvertUnits.ToSimUnits(item.WorldPosition);
            Vector2 end = ConvertUnits.ToSimUnits(target.WorldPosition);
            // Check that there's not other entities that shouldn't be targeted (like a friendly sub) between us and the target.
            Body worldTarget = CheckLineOfSight(start, end);
            bool shoot;
            if (target.Submarine != null)
            {
                start -= target.Submarine.SimPosition;
                end -= target.Submarine.SimPosition;
                Body transformedTarget = CheckLineOfSight(start, end);
                shoot = CanShoot(transformedTarget, user: null, ai, targetSubmarines) && (worldTarget == null || CanShoot(worldTarget, user: null, ai, targetSubmarines));
            }
            else
            {
                shoot = CanShoot(worldTarget, user: null, ai, targetSubmarines);
            }
            if (shoot)
            {
                TryLaunch(deltaTime, ignorePower: true);
            }
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (character.AIController.SelectedAiTarget?.Entity is Character previousTarget &&
                previousTarget.IsDead)
            {
                character.Speak(TextManager.Get("DialogTurretTargetDead"), identifier: "killedtarget" + previousTarget.ID, minDurationBetweenSimilar: 10.0f);
                character.AIController.SelectTarget(null);
            }

            bool canShoot = true;
            if (!HasPowerToShoot())
            {
                var batteries = item.GetConnectedComponents<PowerContainer>();
                float lowestCharge = 0.0f;
                PowerContainer batteryToLoad = null;
                foreach (PowerContainer battery in batteries)
                {
                    if (!battery.Item.IsInteractable(character)) { continue; }
                    if (batteryToLoad == null || battery.Charge < lowestCharge)
                    {
                        batteryToLoad = battery;
                        lowestCharge = battery.Charge;
                    }
                    if (battery.Item.ConditionPercentage <= 0 && AIObjectiveRepairItems.IsValidTarget(battery.Item, character))
                    {
                        if (battery.Item.Repairables.Average(r => r.DegreeOfSuccess(character)) > 0.4f)
                        {
                            objective.AddSubObjective(new AIObjectiveRepairItem(character, battery.Item, objective.objectiveManager, isPriority: true));
                            return false;
                        }
                        else
                        {
                            character.Speak(TextManager.Get("DialogSupercapacitorIsBroken"), identifier: "supercapacitorisbroken", minDurationBetweenSimilar: 30.0f);
                            canShoot = false;
                        }
                    }
                }
                if (batteryToLoad == null) { return true; }
                if (batteryToLoad.RechargeSpeed < batteryToLoad.MaxRechargeSpeed * 0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, objective.objectiveManager, option: "", requireEquip: false));                    
                    return false;
                }
                if (lowestCharge <= 0 && batteryToLoad.Item.ConditionPercentage > 0)
                {
                    character.Speak(TextManager.Get("DialogTurretHasNoPower"), identifier: "turrethasnopower", minDurationBetweenSimilar: 30.0f);
                    canShoot = false;
                }
            }

            int usableProjectileCount = 0;
            int maxProjectileCount = 0;
            foreach (MapEntity e in item.linkedTo)
            {
                if (!item.IsInteractable(character)) { continue; }
                if (!item.prefab.IsLinkAllowed(e.prefab)) { continue; }
                if (e is Item projectileContainer)
                {
                    var container = projectileContainer.GetComponent<ItemContainer>();
                    if (container != null)
                    {
                        maxProjectileCount += container.Capacity;
                        int projectiles = projectileContainer.ContainedItems.Count(it => it.Condition > 0.0f);
                        usableProjectileCount += projectiles;   
                    }                 
                }
            }

            if (usableProjectileCount == 0)
            {
                ItemContainer container = null;
                Item containerItem = null;
                foreach (MapEntity e in item.linkedTo)
                {
                    containerItem = e as Item;
                    if (containerItem == null) { continue; }
                    if (!containerItem.IsInteractable(character)) { continue; }
                    if (character.AIController is HumanAIController aiController && aiController.IgnoredItems.Contains(containerItem)) { continue; }
                    container = containerItem.GetComponent<ItemContainer>();
                    if (container != null) { break; }
                }
                if (container == null || container.ContainableItems.Count == 0)
                {
                    if (character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.GetWithVariable("DialogCannotLoadTurret", "[itemname]", item.Name, formatCapitals: true), identifier: "cannotloadturret", minDurationBetweenSimilar: 30.0f);
                    }
                    return true;
                }
                if (objective.SubObjectives.None())
                {
                    var loadItemsObjective = AIContainItems<Turret>(container, character, objective, usableProjectileCount + 1, equip: true, removeEmpty: true, dropItemOnDeselected: true);
                    loadItemsObjective.ignoredContainerIdentifiers = new string[] { containerItem.prefab.Identifier };
                    if (character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.GetWithVariable("DialogLoadTurret", "[itemname]", item.Name, formatCapitals: true), identifier: "loadturret", minDurationBetweenSimilar: 30.0f);
                    }
                    loadItemsObjective.Abandoned += CheckRemainingAmmo;
                    loadItemsObjective.Completed += CheckRemainingAmmo;
                    return false;

                    void CheckRemainingAmmo()
                    {
                        if (!character.IsOnPlayerTeam) { return; }
                        if (character.Submarine != Submarine.MainSub) { return; }
                        string ammoType = container.ContainableItems.First().Identifiers.FirstOrDefault() ?? "ammobox";
                        int remainingAmmo = Submarine.MainSub.GetItems(false).Count(i => i.HasTag(ammoType) && i.Condition > 1);
                        if (remainingAmmo == 0)
                        {
                            character.Speak(TextManager.Get($"DialogOutOf{ammoType}", fallBackTag: "DialogOutOfTurretAmmo"), identifier: "outofammo", minDurationBetweenSimilar: 30.0f);
                        }
                        else if (remainingAmmo < 3)
                        {
                            character.Speak(TextManager.Get($"DialogLowOn{ammoType}"), identifier: "outofammo", minDurationBetweenSimilar: 30.0f);
                        }
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
            // use full range only if we're actively firing
            if (aiTargetingGraceTimer <= 0f)
            {
                shootDistance *= 0.75f;
            }

            float closestDistance = maxDistance * maxDistance;

            if (currentTarget != null)
            {
                if (currentTarget.Removed || currentTarget.IsDead)
                {
                    currentTarget = null;
                }
            }

            if (aiFindTargetTimer <= 0.0f || currentTarget == null)
            {
                foreach (Character enemy in Character.CharacterList)
                {
                    // Ignore dead, friendly, and those that are inside the same sub
                    if (enemy.IsDead || !enemy.Enabled || enemy.Submarine == character.Submarine) { continue; }
                    if (enemy.Submarine != null && enemy.Submarine.TeamID == character.Submarine.TeamID) { continue; }
                    // Don't aim monsters that are inside any submarine.
                    if (!enemy.IsHuman && enemy.CurrentHull != null) { continue; }
                    if (HumanAIController.IsFriendly(character, enemy)) { continue; }       
                    float dist = Vector2.DistanceSquared(enemy.WorldPosition, item.WorldPosition);
                    if (dist > closestDistance) { continue; }
                    if (dist < shootDistance * shootDistance)
                    {
                        // Only check the angle to targets that are close enough to be shot at
                        // We shouldn't check the angle when a long creature is traveling outside of the shooting range, because doing so would not allow us to shoot the limbs that might be close enough to shoot at.
                        if (!CheckTurretAngle(enemy.WorldPosition)) { continue; }
                    }
                    closestEnemy = enemy;
                    closestDistance = dist;                
                }
                currentTarget = closestEnemy;
                aiFindTargetTimer = aiFindTargetInterval;
            }
            else
            {
                closestEnemy = currentTarget;
            }

            if (closestEnemy != null)
            {
                targetPos = closestEnemy.WorldPosition;
                //if the enemy is inside another sub, aim at the room they're in to make it less obvious that the enemy "knows" exactly where the target is
                if (closestEnemy.Submarine != null && closestEnemy.CurrentHull != null && closestEnemy.Submarine != item.Submarine)
                {
                    targetPos = closestEnemy.CurrentHull.WorldPosition;
                }
                else
                {
                    // Target the closest limb. Doesn't make much difference with smaller creatures, but enables the bots to shoot longer abyss creatures like the endworm. Otherwise they just target the main body = head.
                    float closestDist = closestDistance;
                    foreach (Limb limb in closestEnemy.AnimController.Limbs)
                    {
                        if (limb.IsSevered) { continue; }
                        if (limb.Hidden) { continue; }
                        if (!CheckTurretAngle(limb.WorldPosition)) { continue; }
                        float dist = Vector2.DistanceSquared(limb.WorldPosition, item.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            targetPos = limb.WorldPosition;
                        }
                    }
                    if (closestDist > shootDistance * shootDistance)
                    {
                        // Not close enough to shoot
                        closestEnemy = null;
                        targetPos = null;
                    }
                }
            }
            else if (item.Submarine != null && Level.Loaded != null)
            {
                // Check ice spires
                shootDistance = AIRange * item.OffsetOnSelectedMultiplier;
                closestDistance = shootDistance;
                foreach (var wall in Level.Loaded.ExtraWalls)
                {
                    if (!(wall is DestructibleLevelWall destructibleWall) || destructibleWall.Destroyed) { continue; }
                    foreach (var cell in wall.Cells)
                    {
                        if (cell.DoesDamage)
                        {
                            foreach (var edge in cell.Edges)
                            {
                                Vector2 p1 = edge.Point1 + cell.Translation;
                                Vector2 p2 = edge.Point2 + cell.Translation;
                                Vector2 closestPoint = MathUtils.GetClosestPointOnLineSegment(p1, p2, item.WorldPosition);
                                if (!CheckTurretAngle(closestPoint))
                                {
                                    // The closest point can't be targeted -> get a point directly in front of the turret
                                    Vector2 barrelDir = new Vector2((float)Math.Cos(rotation), -(float)Math.Sin(rotation));
                                    if (MathUtils.GetLineIntersection(p1, p2, item.WorldPosition, item.WorldPosition + barrelDir * shootDistance, out Vector2 intersection))
                                    {
                                        closestPoint = intersection;
                                        if (!CheckTurretAngle(closestPoint)) { continue; }
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                                float dist = Vector2.Distance(closestPoint, item.WorldPosition);
                                if (dist > AIRange + 1000) { continue; }
                                float dot = 0;
                                if (item.Submarine.Velocity != Vector2.Zero)
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
                            }
                        }
                    }
                }
            }

            if (targetPos == null) { return false; }
            // Force the highest priority so that we don't change the objective while targeting enemies.
            objective.ForceHighestPriority = true;

            if (closestEnemy != null && character.AIController.SelectedAiTarget != closestEnemy.AiTarget)
            {
                if (character.IsOnPlayerTeam)
                {
                    if (character.AIController.SelectedAiTarget == null)
                    {
                        if (GameMain.Config.RecentlyEncounteredCreatures.Contains(closestEnemy.SpeciesName))
                        {
                            character.Speak(TextManager.Get("DialogNewTargetSpotted"), null, 0.0f, "newtargetspotted", 30.0f);
                        }
                        else if (GameMain.Config.EncounteredCreatures.Any(name => name.Equals(closestEnemy.SpeciesName, StringComparison.OrdinalIgnoreCase)))
                        {
                            character.Speak(TextManager.GetWithVariable("DialogIdentifiedTargetSpotted", "[speciesname]", closestEnemy.DisplayName), null, 0.0f, "identifiedtargetspotted", 30.0f);
                        }
                        else
                        {
                            character.Speak(TextManager.Get("DialogUnidentifiedTargetSpotted"), null, 0.0f, "unidentifiedtargetspotted", 5.0f);
                        }
                    }
                    else if (GameMain.Config.EncounteredCreatures.None(name => name.Equals(closestEnemy.SpeciesName, StringComparison.OrdinalIgnoreCase)))
                    {
                        character.Speak(TextManager.Get("DialogUnidentifiedTargetSpotted"), null, 0.0f, "unidentifiedtargetspotted", 5.0f);
                    }
                    character.AddEncounter(closestEnemy);
                }
                character.AIController.SelectTarget(closestEnemy.AiTarget);
            }
            else if (closestEnemy == null && character.IsOnPlayerTeam)
            {
                character.Speak(TextManager.Get("DialogIceSpireSpotted"), null, 0.0f, "icespirespotted", 60.0f);
            }

            character.CursorPosition = targetPos.Value;
            if (character.Submarine != null) 
            { 
                character.CursorPosition -= character.Submarine.Position; 
            }
            
            float enemyAngle = MathUtils.VectorToAngle(targetPos.Value - item.WorldPosition);
            float turretAngle = -rotation;

            float maxAngleError = 0.15f;
            if (MaxChargeTime > 0.0f && currentChargingState == ChargingState.WindingUp && FiringRotationSpeedModifier > 0.0f)
            {
                //larger margin of error if the weapon needs to be charged (-> the bot can start charging when the turret is still rotating towards the target)
                maxAngleError *= 2.0f;
            }

            if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > maxAngleError) { return false; }

            if (canShoot)
            {
                Vector2 start = ConvertUnits.ToSimUnits(item.WorldPosition);
                Vector2 end = ConvertUnits.ToSimUnits(targetPos.Value);
                // Check that there's not other entities that shouldn't be targeted (like a friendly sub) between us and the target.
                Body worldTarget = CheckLineOfSight(start, end);
                bool shoot;
                if (closestEnemy != null && closestEnemy.Submarine != null)
                {
                    start -= closestEnemy.Submarine.SimPosition;
                    end -= closestEnemy.Submarine.SimPosition;
                    Body transformedTarget = CheckLineOfSight(start, end);
                    shoot = CanShoot(transformedTarget, character) && (worldTarget == null || CanShoot(worldTarget, character));
                }
                else
                {
                    shoot = CanShoot(worldTarget, character);
                }
                if (!shoot) { return false; }
                if (character.IsOnPlayerTeam)
                {
                    character.Speak(TextManager.Get("DialogFireTurret"), null, 0.0f, "fireturret", 10.0f);
                }
                character.SetInput(InputType.Shoot, true, true);
            }
            aiTargetingGraceTimer = 5f;
            return false;
        }

        private bool CanShoot(Body targetBody, Character user = null, WreckAI ai = null, bool targetSubmarines = true)
        {
            if (targetBody == null) { return false; }
            Character targetCharacter = null;
            if (targetBody.UserData is Character c)
            {
                targetCharacter = c;
            }
            else if (targetBody.UserData is Limb limb)
            {
                targetCharacter = limb.character;
            }
            if (targetCharacter != null)
            {
                if (user != null)
                {
                    if (HumanAIController.IsFriendly(user, targetCharacter))
                    {
                        return false;
                    }
                }
                if (ai != null)
                {
                    if (targetCharacter.Params.Group.Equals(ai.Config.Entity, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (targetBody.UserData is ISpatialEntity e)
                {
                    Submarine sub = e.Submarine ?? e as Submarine;
                    if (!targetSubmarines && e is Submarine) { return false; }
                    if (sub == null) { return false; }
                    if (sub == Item.Submarine) { return false; }
                    if (sub.Info.IsOutpost || sub.Info.IsWreck || sub.Info.IsBeacon) { return false; }
                    if (sub.TeamID == Item.Submarine.TeamID) { return false; }
                }
                else if (!(targetBody.UserData is Voronoi2.VoronoiCell cell && cell.IsDestructible))
                {
                    // Hit something else, probably a level wall
                    return false;
                }
            }
            return true;
        }

        private Body CheckLineOfSight(Vector2 start, Vector2 end)
        {
            var collisionCategories = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel;
            Body pickedBody = Submarine.PickBody(start, end, null, collisionCategories, allowInsideFixture: true,
               customPredicate: (Fixture f) =>
               {
                   if (f.UserData is Item i && i.GetComponent<Turret>() != null) { return false; }
                   return !item.StaticFixtures.Contains(f);
               });
            return pickedBody;
        }

        private Vector2 GetRelativeFiringPosition(bool useOffset = true)
        {
            Vector2 transformedFiringOffset = Vector2.Zero;
            if (useOffset)
            {
                transformedFiringOffset = MathUtils.RotatePoint(new Vector2(-FiringOffset.Y, -FiringOffset.X) * item.Scale, -rotation);
            }
            return new Vector2(item.WorldRect.X + transformedBarrelPos.X + transformedFiringOffset.X, item.WorldRect.Y - transformedBarrelPos.Y + transformedFiringOffset.Y);
        }

        private bool CheckTurretAngle(float angle)
        {
            float midRotation = (minRotation + maxRotation) / 2.0f;
            while (midRotation - angle < -MathHelper.Pi) { angle -= MathHelper.TwoPi; }
            while (midRotation - angle > MathHelper.Pi) { angle += MathHelper.TwoPi; }
            return angle >= minRotation && angle <= maxRotation;
        }

        private bool CheckTurretAngle(Vector2 target) => CheckTurretAngle(-MathUtils.VectorToAngle(target - item.WorldPosition));

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            barrelSprite?.Remove(); barrelSprite = null;
            railSprite?.Remove(); railSprite = null;

#if CLIENT
            crosshairSprite?.Remove(); crosshairSprite = null;
            crosshairPointerSprite?.Remove(); crosshairPointerSprite = null;
            moveSoundChannel?.Dispose(); moveSoundChannel = null;
#endif
        }

        private List<Projectile> GetLoadedProjectiles()
        {
            List<Projectile> projectiles = new List<Projectile>();
            // check the item itself first
            CheckProjectileContainer(item, projectiles, out bool _);
            foreach (MapEntity e in item.linkedTo)
            {
                if (!item.prefab.IsLinkAllowed(e.prefab)) { continue; }
                if (e is Item projectileContainer)
                {
                    CheckProjectileContainer(projectileContainer, projectiles, out bool stopSearching);
                    if (projectiles.Any() || stopSearching) { return projectiles; }
                }
            }

            return projectiles;
        }

        private void CheckProjectileContainer(Item projectileContainer, List<Projectile> projectiles, out bool stopSearching)
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
            minRotation = MathHelper.Pi - minRotation;
            maxRotation = MathHelper.Pi - maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            barrelPos.X = item.Rect.Width / item.Scale - barrelPos.X;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
            rotation = (minRotation + maxRotation) / 2;

            UpdateTransformedBarrelPos();
        }

        public override void FlipY(bool relativeToSub)
        {
            BaseRotation = MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(MathHelper.ToRadians(180 - BaseRotation)));

            minRotation = -minRotation;
            maxRotation = -maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
            rotation = (minRotation + maxRotation) / 2;

            UpdateTransformedBarrelPos();
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
                    }
                    user = sender;
                    resetUserTimer = 10.0f;
                    break;
                case "trigger_in":
                    if (signal.value == "0") { return; }
                    item.Use((float)Timing.Step, sender);
                    user = sender;
                    resetUserTimer = 10.0f;
                    //triggering the Use method through item.Use will fail if the item is not characterusable and the signal was sent by a character
                    //so lets do it manually
                    if (!characterUsable && sender != null)
                    {
                        TryLaunch((float)Timing.Step, sender);
                    }
                    break;
                case "toggle_light":
                    if (lightComponent != null && signal.value != "0")
                    {
                        lightComponent.IsOn = !lightComponent.IsOn;
                    }
                    break;
                case "set_light":
                    if (lightComponent != null)
                    {
                        lightComponent.IsOn = signal.value != "0";
                    }
                    break;
            }
        }

        private Vector2? loadedRotationLimits;
        private float? loadedBaseRotation;
        public override void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            loadedRotationLimits = componentElement.GetAttributeVector2("rotationlimits", RotationLimits);
            loadedBaseRotation = componentElement.GetAttributeFloat("baserotation", componentElement.Parent.GetAttributeFloat("rotation", BaseRotation));
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            FindLightComponent();
            if (!loadedBaseRotation.HasValue)
            {
                if (item.FlippedX) { FlipX(relativeToSub: false); }
                if (item.FlippedY) { FlipY(relativeToSub: false); }
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            if (extraData.Length > 2)
            {
                msg.Write(!(extraData[2] is Item item) ? ushort.MaxValue : item.ID);
                msg.WriteRangedSingle(MathHelper.Clamp(rotation, minRotation, maxRotation), minRotation, maxRotation, 16);
            }
            else
            {
                msg.Write((ushort)0);
                float wrappedTargetRotation = targetRotation;
                while (wrappedTargetRotation < minRotation && MathUtils.IsValid(wrappedTargetRotation))
                {
                    wrappedTargetRotation += MathHelper.TwoPi;
                }
                while (wrappedTargetRotation > maxRotation && MathUtils.IsValid(wrappedTargetRotation))
                {
                    wrappedTargetRotation -= MathHelper.TwoPi;
                }
                msg.WriteRangedSingle(MathHelper.Clamp(wrappedTargetRotation, minRotation, maxRotation), minRotation, maxRotation, 16);
            }
        }
    }
}


