using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite barrelSprite, railSprite;

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

        private Character user;
        
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

        [Editable(0.0f, 1000.0f), Serialize(5.0f, false, description: "The period of time the user has to wait between shots.")]
        public float Reload
        {
            get { return reloadTime; }
            set { reloadTime = value; }
        }

        [Editable, Serialize("0.0,0.0", true, description: "The range at which the barrel can rotate. TODO")]
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

        private float baseRotationRad;
        [Editable(0.0f, 360.0f), Serialize(0.0f, true, description: "The angle of the turret's base in degrees.")]
        public float BaseRotation
        {
            get { return MathHelper.ToDegrees(baseRotationRad); }
            set
            {
                baseRotationRad = MathHelper.ToRadians(value);
                UpdateTransformedBarrelPos();
            }
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
                }
            }
            item.IsShootable = true;
            item.RequireAimToUse = false;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        private void UpdateTransformedBarrelPos()
        {
            float flippedRotation = BaseRotation;
            if (item.FlippedX) flippedRotation = -flippedRotation;
            //if (item.FlippedY) flippedRotation = 180.0f - flippedRotation;
            transformedBarrelPos = MathUtils.RotatePointAroundTarget(barrelPos * item.Scale, new Vector2(item.Rect.Width / 2, item.Rect.Height / 2), flippedRotation);
#if CLIENT
            item.SpriteRotation = MathHelper.ToRadians(flippedRotation);
#endif
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            var lightComponents = item.GetComponents<LightComponent>();
            if (lightComponents != null && lightComponents.Count() > 0)
            {
                lightComponent = lightComponents.FirstOrDefault(lc => lc.Parent == this);
#if CLIENT
                if (lightComponent != null) 
                {
                    lightComponent.Parent = null;
                    lightComponent.Rotation = rotation;
                    lightComponent.Light.Rotation = -rotation;
                }
#endif
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            this.cam = cam;

            if (reload > 0.0f) reload -= deltaTime;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            UpdateProjSpecific(deltaTime);

            if (minRotation == maxRotation) return;

            float targetMidDiff = MathHelper.WrapAngle(targetRotation - (minRotation + maxRotation) / 2.0f);

            float maxDist = (maxRotation - minRotation) / 2.0f;

            if (Math.Abs(targetMidDiff) > maxDist)
            {
                targetRotation = (targetMidDiff < 0.0f) ? minRotation : maxRotation;
            }

            float degreeOfSuccess = user == null ? 0.5f : DegreeOfSuccess(user);            
            if (degreeOfSuccess < 0.5f) degreeOfSuccess *= degreeOfSuccess; //the ease of aiming drops quickly with insufficient skill levels
            float springStiffness = MathHelper.Lerp(SpringStiffnessLowSkill, SpringStiffnessHighSkill, degreeOfSuccess);
            float springDamping = MathHelper.Lerp(SpringDampingLowSkill, SpringDampingHighSkill, degreeOfSuccess);
            float rotationSpeed = MathHelper.Lerp(RotationSpeedLowSkill, RotationSpeedHighSkill, degreeOfSuccess);

            angularVelocity += 
                (MathHelper.WrapAngle(targetRotation - rotation) * springStiffness - angularVelocity * springDamping) * deltaTime;
            angularVelocity = MathHelper.Clamp(angularVelocity, -rotationSpeed, rotationSpeed);

            rotation += angularVelocity * deltaTime;

            float rotMidDiff = MathHelper.WrapAngle(rotation - (minRotation + maxRotation) / 2.0f);

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

            if (lightComponent != null)
            {
                lightComponent.Rotation = rotation;
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!characterUsable && character != null) return false;
            return TryLaunch(deltaTime, character);
        }

        private bool TryLaunch(float deltaTime, Character character = null)
        {
#if CLIENT
            if (GameMain.Client != null) return false;
#endif

            if (reload > 0.0f) return false;

            if (GetAvailableBatteryPower() < powerConsumption)
            {
#if CLIENT
                if (!flashLowPower && character != null && character == Character.Controlled)
                {
                    flashLowPower = true;
                    GUI.PlayUISound(GUISoundType.PickItemFail);
                }
#endif
                return false;
            }
            
            foreach (MapEntity e in item.linkedTo)
            {
                //use linked projectile containers in case they have to react to the turret being launched somehow
                //(play a sound, spawn more projectiles)
                if (!(e is Item linkedItem)) continue;
                ItemContainer projectileContainer = linkedItem.GetComponent<ItemContainer>();
                if (projectileContainer != null)
                {
                    linkedItem.Use(deltaTime, null);
                    var repairable = linkedItem.GetComponent<Repairable>();
                    if (repairable != null)
                    {
                        repairable.LastActiveTime = (float)Timing.TotalTime + 1.0f;
                    }
                }
            }

            var projectiles = GetLoadedProjectiles(true);
            if (projectiles.Count == 0)
            {
                //coilguns spawns ammo in the ammo boxes with the OnUse statuseffect when the turret is launched,
                //causing a one frame delay before the gun can be launched (or more in multiplayer where there may be a longer delay)
                //  -> attempt to launch the gun multiple times before showing the "no ammo" flash
                failedLaunchAttempts++;
#if CLIENT
                if (!flashNoAmmo && character != null && character == Character.Controlled && failedLaunchAttempts > 20)
                {
                    flashNoAmmo = true;
                    failedLaunchAttempts = 0;
                    GUI.PlayUISound(GUISoundType.PickItemFail);
                }
#endif
                return false;
            }

            failedLaunchAttempts = 0;

            var batteries = item.GetConnectedComponents<PowerContainer>();
            float neededPower = powerConsumption;

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
                    if (GameMain.Server != null)
                    {
                        battery.Item.CreateServerEvent(battery);
                    }
#endif
                }
            }

            Launch(projectiles[0].Item, character);

#if SERVER
            if (character != null)
            {
                string msg = character.LogName + " launched " + item.Name + " (projectile: " + projectiles[0].Item.Name;
                var containedItems = projectiles[0].Item.ContainedItems;
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

        private void Launch(Item projectile, Character user = null)
        {
            reload = reloadTime;

            projectile.Drop(null);
            projectile.body.Dir = 1.0f;

            projectile.body.ResetDynamics();
            projectile.body.Enabled = true;
            projectile.SetTransform(ConvertUnits.ToSimUnits(new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y)), -rotation);
            projectile.UpdateTransform();
            projectile.Submarine = projectile.body.Submarine;

            Projectile projectileComponent = projectile.GetComponent<Projectile>();
            if (projectileComponent != null)
            {
                projectileComponent.Use((float)Timing.Step);
                projectileComponent.User = user;
            }

            if (projectile.Container != null) projectile.Container.RemoveContained(projectile);
            
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), projectile });
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f, user: user);
            LaunchProjSpecific();
        }

        partial void LaunchProjSpecific();

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (character.AIController.SelectedAiTarget?.Entity is Character previousTarget &&
                previousTarget.IsDead)
            {
                character?.Speak(TextManager.Get("DialogTurretTargetDead"), null, 0.0f, "killedtarget" + previousTarget.ID, 30.0f);
                character.AIController.SelectTarget(null);
            }

            if (GetAvailableBatteryPower() < powerConsumption)
            {
                var batteries = item.GetConnectedComponents<PowerContainer>();

                float lowestCharge = 0.0f;
                PowerContainer batteryToLoad = null;
                foreach (PowerContainer battery in batteries)
                {
                    if (batteryToLoad == null || battery.Charge < lowestCharge)
                    {
                        batteryToLoad = battery;
                        lowestCharge = battery.Charge;
                    }
                }

                if (batteryToLoad == null) return true;

                if (batteryToLoad.RechargeSpeed < batteryToLoad.MaxRechargeSpeed * 0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, objective.objectiveManager, option: "", requireEquip: false));                    
                    return false;
                }
            }

            int usableProjectileCount = 0;
            int maxProjectileCount = 0;
            foreach (MapEntity e in item.linkedTo)
            {
                if (!(e is Item projectileContainer)) continue;

                var containedItems = projectileContainer.ContainedItems;
                if (containedItems != null)
                {
                    var container = projectileContainer.GetComponent<ItemContainer>();
                    maxProjectileCount += container.Capacity;

                    int projectiles = containedItems.Count(it => it.Condition > 0.0f);
                    usableProjectileCount += projectiles;
                }
            }

            if (usableProjectileCount == 0 || (usableProjectileCount < maxProjectileCount && objective.Option.Equals("fireatwill", StringComparison.OrdinalIgnoreCase)))
            {
                ItemContainer container = null;
                Item containerItem = null;
                foreach (MapEntity e in item.linkedTo)
                {
                    containerItem = e as Item;
                    if (containerItem == null) { continue; }
                    container = containerItem.GetComponent<ItemContainer>();
                    if (container != null) { break; }
                }
                if (container == null || container.ContainableItems.Count == 0) { return true; }

                if (objective.SubObjectives.None())
                {
                    if (!AIDecontainEmptyItems(character, objective, equip: true, sourceContainer: container))
                    {
                        return false;
                    }
                }
                if (objective.SubObjectives.None())
                {
                    var loadItemsObjective = AIContainItems<Turret>(container, character, objective, usableProjectileCount + 1, equip: true, removeEmpty: true);
                    loadItemsObjective.ignoredContainerIdentifiers = new string[] { containerItem.prefab.Identifier };
                    character.Speak(TextManager.GetWithVariable("DialogLoadTurret", "[itemname]", item.Name, true), null, 0.0f, "loadturret", 30.0f);
                }
                return false;
            }

            //enough shells and power
            Character closestEnemy = null;
            float closestDist = 3000 * 3000;
            foreach (Character enemy in Character.CharacterList)
            {
                // Ignore friendly and those that are inside the sub
                if (enemy.IsDead || enemy.AnimController.CurrentHull != null || !enemy.Enabled) { continue; }
                if (HumanAIController.IsFriendly(character, enemy)) { continue; }
                
                float dist = Vector2.DistanceSquared(enemy.WorldPosition, item.WorldPosition);
                if (dist > closestDist) { continue; }
                
                float angle = -MathUtils.VectorToAngle(enemy.WorldPosition - item.WorldPosition);
                float midRotation = (minRotation + maxRotation) / 2.0f;
                while (midRotation - angle < -MathHelper.Pi) { angle -= MathHelper.TwoPi; }
                while (midRotation - angle > MathHelper.Pi) { angle += MathHelper.TwoPi; }

                if (angle < minRotation || angle > maxRotation) { continue; }

                closestEnemy = enemy;
                closestDist = dist;                
            }

            if (closestEnemy == null) { return false; }
            
            character.AIController.SelectTarget(closestEnemy.AiTarget);

            character.CursorPosition = closestEnemy.WorldPosition;
            if (item.Submarine != null) { character.CursorPosition -= item.Submarine.Position; }
            
            float enemyAngle = MathUtils.VectorToAngle(closestEnemy.WorldPosition - item.WorldPosition);
            float turretAngle = -rotation;

            if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.15f) { return false; }

            var pickedBody = Submarine.PickBody(ConvertUnits.ToSimUnits(item.WorldPosition), closestEnemy.SimPosition);
            if (pickedBody == null) { return false; }
            Character target = null;
            if (pickedBody.UserData is Character c)
            {
                target = c;
            }
            else if (pickedBody.UserData is Limb limb)
            {
                target = limb.character;
            }
            if (target == null || HumanAIController.IsFriendly(character, target))
            {
                return false;
            }

            if (objective.Option.ToLowerInvariant() == "fireatwill")
            {
                character?.Speak(TextManager.GetWithVariable("DialogFireTurret", "[itemname]", item.Name, true), null, 0.0f, "fireturret", 5.0f);
                character.SetInput(InputType.Shoot, true, true);
            }

            return false;
        }

        private void GetAvailablePower(out float availableCharge, out float availableCapacity)
        {
            var batteries = item.GetConnectedComponents<PowerContainer>();

            availableCharge = 0.0f;
            availableCapacity = 0.0f;
            foreach (PowerContainer battery in batteries)
            {
                availableCharge += battery.Charge;
                availableCapacity += battery.Capacity;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            barrelSprite?.Remove(); barrelSprite = null;
            railSprite?.Remove(); railSprite = null;

#if CLIENT
            moveSoundChannel?.Dispose(); moveSoundChannel = null;
#endif
        }

        private List<Projectile> GetLoadedProjectiles(bool returnFirst = false)
        {
            List<Projectile> projectiles = new List<Projectile>();
            //check the item itself first
            CheckProjectileContainer(item, projectiles, returnFirst);
            foreach (MapEntity e in item.linkedTo)
            {
                if (e is Item projectileContainer) { CheckProjectileContainer(projectileContainer, projectiles, returnFirst); }
                if (returnFirst && projectiles.Any()) return projectiles;
            }

            return projectiles;
        }

        private void CheckProjectileContainer(Item projectileContainer, List<Projectile> projectiles, bool returnFirst)
        {
            var containedItems = projectileContainer.ContainedItems;
            if (containedItems == null) return;

            foreach (Item containedItem in containedItems)
            {
                var projectileComponent = containedItem.GetComponent<Projectile>();
                if (projectileComponent != null)
                {
                    projectiles.Add(projectileComponent);
                    if (returnFirst) return;
                }
                else
                {
                    //check if the contained item is another itemcontainer with projectiles inside it
                    if (containedItem.ContainedItems == null) continue;
                    foreach (Item subContainedItem in containedItem.ContainedItems)
                    {
                        projectileComponent = subContainedItem.GetComponent<Projectile>();
                        if (projectileComponent != null)
                        {
                            projectiles.Add(projectileComponent);
                            if (returnFirst) return;
                        }
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
            baseRotationRad = MathUtils.WrapAngleTwoPi(baseRotationRad - MathHelper.Pi);
            UpdateTransformedBarrelPos();

            /*minRotation = -minRotation;
            maxRotation = -maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            barrelPos.Y = item.Rect.Height / item.Scale - barrelPos.Y;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
            rotation = (minRotation + maxRotation) / 2;

            UpdateTransformedBarrelPos();*/
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "position_in":
                    if (float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float newRotation))
                    {
                        targetRotation = MathHelper.ToRadians(newRotation);
                        IsActive = true;
                    }
                    user = sender;
                    break;
                case "trigger_in":
                    item.Use((float)Timing.Step, sender);
                    user = sender;
                    //triggering the Use method through item.Use will fail if the item is not characterusable and the signal was sent by a character
                    //so lets do it manually
                    if (!characterUsable && sender != null)
                    {
                        TryLaunch((float)Timing.Step, sender);
                    }
                    break;
                case "toggle":
                case "toggle_light":
                    if (lightComponent != null)
                    {
                        lightComponent.IsOn = !lightComponent.IsOn;
                    }
                    break;
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            Item item = (Item)extraData[2];
            msg.Write(item.Removed ? (ushort)0 : item.ID);
        }
    }
}


