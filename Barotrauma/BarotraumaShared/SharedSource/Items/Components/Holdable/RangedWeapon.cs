using Barotrauma.Abilities;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class RangedWeapon : ItemComponent
    {
        private float reload;
        public float ReloadTimer { get; private set; }

        private Vector2 barrelPos;

        [Serialize("0.0,0.0", IsPropertySaveable.No, description: "The position of the barrel as an offset from the item's center (in pixels). Determines where the projectiles spawn.")]
        public string BarrelPos
        {
            get { return XMLExtensions.Vector2ToString(ConvertUnits.ToDisplayUnits(barrelPos)); }
            set { barrelPos = ConvertUnits.ToSimUnits(XMLExtensions.ParseVector2(value)); }
        }

        [Serialize(1.0f, IsPropertySaveable.No, description: "How long the user has to wait before they can fire the weapon again (in seconds).")]
        public float Reload
        {
            get { return reload; }
            set { reload = Math.Max(value, 0.0f); }
        }

        [Serialize(0f, IsPropertySaveable.No, description: "Weapons skill requirement to reload at normal speed.")]
        public float ReloadSkillRequirement
        {
            get;
            set;
        }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Reload time at 0 skill level. Reload time scales with skill level up to the Weapons skill requirement.")]
        public float ReloadNoSkill
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Tells the AI to hold the trigger down when it uses this weapon")]
        public bool HoldTrigger
        {
            get;
            set;
        }

        [Serialize(1, IsPropertySaveable.No, description: "How many projectiles the weapon launches when fired once.")]
        public int ProjectileCount
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Random spread applied to the firing angle of the projectiles when used by a character with sufficient skills to use the weapon (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Random spread applied to the firing angle of the projectiles when used by a character with insufficient skills to use the weapon (in degrees).")]
        public float UnskilledSpread
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The impulse applied to the physics body of the projectile (the higher the impulse, the faster the projectiles are launched). Sum of weapon + projectile.")]
        public float LaunchImpulse
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Percentage of damage mitigation ignored when hitting armored body parts (deflecting limbs). Sum of weapon + projectile."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1f)]
        public float Penetration { get; private set; }

        [Serialize(1f, IsPropertySaveable.Yes, description: "Weapon's damage modifier")]
        public float WeaponDamageModifier
        {
            get;
            private set;
        }

        [Serialize(0f, IsPropertySaveable.Yes, description: "The time required for a charge-type turret to charge up before able to fire.")]
        public float MaxChargeTime
        {
            get;
            private set;
        }

        private enum ChargingState
        {
            Inactive,
            WindingUp,
            WindingDown,
        }
        private ChargingState currentChargingState;

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body == null ? item.RotationRad : item.body.Rotation);
                Vector2 flippedPos = barrelPos;
                if (item.body != null && item.body.Dir < 0.0f) { flippedPos.X = -flippedPos.X; }
                return Vector2.Transform(flippedPos, bodyTransform) * item.Scale;
            }
        }


        public Projectile LastProjectile { get; private set; }

        private float currentChargeTime;
        private bool tryingToCharge;

        public RangedWeapon(Item item, ContentXElement element)
            : base(item, element)
        {
            item.IsShootable = true;
            // TODO: should define this in xml if we have ranged weapons that don't require aim to use
            item.RequireAimToUse = true;
            characterUsable = true;

            if (ReloadSkillRequirement > 0 && ReloadNoSkill <= reload)
            {
                DebugConsole.AddWarning($"Invalid XML at {item.Name}: ReloadNoSkill is lower or equal than it's reload skill, despite having ReloadSkillRequirement.");
            }

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override void Equip(Character character)
        {
            ReloadTimer = Math.Min(reload, 1.0f);
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            ReloadTimer -= deltaTime;

            if (ReloadTimer < 0.0f)
            {
                ReloadTimer = 0.0f;
                if (MaxChargeTime <= 0f)
                {
                    IsActive = false;
                    return;
                }
            }

            float previousChargeTime = currentChargeTime;

            float chargeDeltaTime = tryingToCharge && ReloadTimer <= 0f ? deltaTime : -deltaTime;
            currentChargeTime = Math.Clamp(currentChargeTime + chargeDeltaTime, 0f, MaxChargeTime);

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
        }

        partial void UpdateProjSpecific(float deltaTime);

        private float GetSpread(Character user)
        {
            float degreeOfFailure = MathHelper.Clamp(1.0f - DegreeOfSuccess(user), 0.0f, 1.0f);
            degreeOfFailure *= degreeOfFailure;
            float spread = MathHelper.Lerp(Spread, UnskilledSpread, degreeOfFailure) / (1f + user.GetStatValue(StatTypes.RangedSpreadReduction));
            return MathHelper.ToRadians(spread);
        }

        private readonly List<Body> ignoredBodies = new List<Body>();
        public override bool Use(float deltaTime, Character character = null)
        {
            tryingToCharge = true;
            if (character == null || character.Removed) { return false; }
            if ((item.RequireAimToUse && !character.IsKeyDown(InputType.Aim)) || ReloadTimer > 0.0f) { return false; }
            if (currentChargeTime < MaxChargeTime) { return false; }

            IsActive = true;
            float baseReloadTime = reload;
            float weaponSkill = character.GetSkillLevel("weapons");
            if (ReloadSkillRequirement > 0 && ReloadNoSkill > reload && weaponSkill < ReloadSkillRequirement)
            {
                //Examples, assuming 40 weapon skill required: 1 - 40/40 = 0 ... 1 - 0/40 = 1 ... 1 - 20 / 40 = 0.5
                float reloadFailure = MathHelper.Clamp(1 - (weaponSkill / ReloadSkillRequirement), 0, 1);
                baseReloadTime = MathHelper.Lerp(reload, ReloadNoSkill, reloadFailure);
            }
            ReloadTimer = baseReloadTime / (1 + character?.GetStatValue(StatTypes.RangedAttackSpeed) ?? 0f);
            ReloadTimer /= 1f + item.GetQualityModifier(Quality.StatType.FiringRateMultiplier);

            currentChargeTime = 0f;

            if (character != null)
            {
                var abilityRangedWeapon = new AbilityRangedWeapon(item);
                character.CheckTalents(AbilityEffectType.OnUseRangedWeapon, abilityRangedWeapon);
            }

            if (item.AiTarget != null)
            {
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
            }

            ignoredBodies.Clear();
            foreach (Limb l in character.AnimController.Limbs)
            {
                if (l.IsSevered) { continue; }
                ignoredBodies.Add(l.body.FarseerBody);
            }

            foreach (Item heldItem in character.HeldItems)
            {
                var holdable = heldItem.GetComponent<Holdable>();
                if (holdable?.Pusher != null)
                {
                    ignoredBodies.Add(holdable.Pusher.FarseerBody);
                }
            }

            float degreeOfFailure = 1.0f - DegreeOfSuccess(character);
            degreeOfFailure *= degreeOfFailure;
            if (degreeOfFailure > Rand.Range(0.0f, 1.0f))
            {
                ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);
            }

            for (int i = 0; i < ProjectileCount; i++)
            {
                Projectile projectile = FindProjectile(triggerOnUseOnContainers: true);
                if (projectile != null)
                {
                    Vector2 barrelPos = TransformedBarrelPos + item.body.SimPosition;
                    float rotation = (Item.body.Dir == 1.0f) ? Item.body.Rotation : Item.body.Rotation - MathHelper.Pi;
                    float spread = GetSpread(character) * Rand.Range(-0.5f, 0.5f);
                    var lastProjectile = LastProjectile;
                    if (lastProjectile != projectile)
                    {
                        lastProjectile?.Item.GetComponent<Rope>()?.Snap();
                    }
                    float damageMultiplier = (1f + item.GetQualityModifier(Quality.StatType.FirepowerMultiplier)) * WeaponDamageModifier;
                    projectile.Launcher = item;
                    projectile.Shoot(character, character.AnimController.AimSourceSimPos, barrelPos, rotation + spread, ignoredBodies: ignoredBodies.ToList(), createNetworkEvent: false, damageMultiplier, LaunchImpulse);
                    projectile.Item.GetComponent<Rope>()?.Attach(Item, projectile.Item);
                    if (projectile.Item.body != null)
                    {
                        if (i == 0)
                        {
                            Item.body.ApplyLinearImpulse(new Vector2((float)Math.Cos(projectile.Item.body.Rotation), (float)Math.Sin(projectile.Item.body.Rotation)) * Item.body.Mass * -50.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                        }
                        projectile.Item.body.ApplyTorque(projectile.Item.body.Mass * degreeOfFailure * Rand.Range(-10.0f, 10.0f));
                    }
                    Item.RemoveContained(projectile.Item);
                }
                LastProjectile = projectile;
            }

            LaunchProjSpecific();

            return true;
        }

        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            return characterUsable || character == null;
        }

        public Projectile FindProjectile(bool triggerOnUseOnContainers = false)
        {
            var containedItems = item.OwnInventory?.AllItemsMod;
            if (containedItems == null) { return null; }

            foreach (Item item in containedItems)
            {
                if (item == null) { continue; }
                Projectile projectile = item.GetComponent<Projectile>();
                if (projectile != null) { return projectile; }
            }

            //projectile not found, see if one of the contained items contains projectiles
            foreach (Item it in containedItems)
            {
                if (it == null) { continue; }
                var containedSubItems = it.OwnInventory?.AllItemsMod;
                if (containedSubItems == null) { continue; }
                foreach (Item subItem in containedSubItems)
                {
                    if (subItem == null) { continue; }
                    Projectile projectile = subItem.GetComponent<Projectile>();
                    //apply OnUse statuseffects to the container in case it has to react to it somehow
                    //(play a sound, spawn more projectiles, reduce condition...)
                    if (triggerOnUseOnContainers && subItem.Condition > 0.0f)
                    {
                        subItem.GetComponent<ItemContainer>()?.Item.ApplyStatusEffects(ActionType.OnUse, 1.0f);
                    }
                    if (projectile != null) { return projectile; }
                }
            }
            
            return null;
        }

        partial void LaunchProjSpecific();
    }
    class AbilityRangedWeapon : AbilityObject, IAbilityItem
    {
        public AbilityRangedWeapon(Item item)
        {
            Item = item;
        }
        public Item Item { get; set; }
    }
}
