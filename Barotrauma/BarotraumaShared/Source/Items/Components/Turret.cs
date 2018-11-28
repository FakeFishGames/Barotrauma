using Barotrauma.Networking;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite barrelSprite, railSprite;

        private Vector2 barrelPos;
        private Vector2 transformedBarrelPos;

        private bool? hasLight;
        private LightComponent lightComponent;

        private float baseAngle;

        private float rotation, targetRotation;

        private float reload, reloadTime;

        private float minRotation, maxRotation;

        private float launchImpulse;

        private Camera cam;

        private float angularVelocity;

        private Character user;
        
        [Serialize("0,0", false)]
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

        [Serialize(0.0f, false)]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [Serialize(5.0f, false), Editable(0.0f, 1000.0f)]
        public float Reload
        {
            get { return reloadTime; }
            set { reloadTime = value; }
        }

        [Serialize("0.0,0.0", true), Editable]
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
            }
        }

        [Serialize(5.0f, false), Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringStiffnessLowSkill
        {
            get;
            private set;
        }
        [Serialize(2.0f, false), Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringStiffnessHighSkill
        {
            get;
            private set;
        }

        [Serialize(50.0f, false), Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringDampingLowSkill
        {
            get;
            private set;
        }
        [Serialize(10.0f, false), Editable(0.0f, 1000.0f, DecimalCount = 2)]
        public float SpringDampingHighSkill
        {
            get;
            private set;
        }

        [Serialize(1.0f, false), Editable(0.0f, 100.0f, DecimalCount = 2)]
        public float RotationSpeedLowSkill
        {
            get;
            private set;
        }
        [Serialize(5.0f, false), Editable(0.0f, 100.0f, DecimalCount = 2)]
        public float RotationSpeedHighSkill
        {
            get;
            private set;
        }

        private float baseRotationRad;
        [Serialize(0.0f, true), Editable(0.0f, 360.0f)]
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

            hasLight = null;

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        private void UpdateTransformedBarrelPos()
        {
            float flippedRotation = BaseRotation;
            if (item.FlippedX) flippedRotation = -flippedRotation;
            if (item.FlippedY) flippedRotation = 180.0f - flippedRotation;
            transformedBarrelPos = MathUtils.RotatePointAroundTarget(barrelPos * item.Scale, new Vector2(item.Rect.Width / 2, item.Rect.Height / 2), flippedRotation);
#if CLIENT
            item.SpriteRotation = MathHelper.ToRadians(flippedRotation);
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (hasLight == null)
            {
                var lightComponents = item.GetComponents<LightComponent>();
                
                if (lightComponents != null && lightComponents.Count() > 0)
                {
                    lightComponent = lightComponents.FirstOrDefault(lc => lc.Parent == this);
                    hasLight = (lightComponent != null);
                }
                else
                {
                    hasLight = false;
                }
            }

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

            if ((bool)hasLight)
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
            if (GameMain.Client != null) return false;

            if (reload > 0.0f) return false;

            if (GetAvailablePower() < powerConsumption)
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
                Item linkedItem = e as Item;
                if (linkedItem == null) continue;
                ItemContainer projectileContainer = linkedItem.GetComponent<ItemContainer>();
                if (projectileContainer != null) linkedItem.Use(deltaTime, null);
            }

            var projectiles = GetLoadedProjectiles(true);
            if (projectiles.Count == 0)
            {
#if CLIENT
                if (!flashNoAmmo && character != null && character == Character.Controlled)
                {
                    flashNoAmmo = true;
                    GUI.PlayUISound(GUISoundType.PickItemFail);
                }
#endif
                return false;
            }

            var batteries = item.GetConnectedComponents<PowerContainer>();
            float availablePower = 0.0f;
            foreach (PowerContainer battery in batteries)
            {
                float batteryPower = Math.Min(battery.Charge * 3600.0f, battery.MaxOutPut);
                float takePower = Math.Min(powerConsumption - availablePower, batteryPower);

                battery.Charge -= takePower / 3600.0f;

                if (GameMain.Server != null)
                {
                    battery.Item.CreateServerEvent(battery);
                }
            }

            Launch(projectiles[0].Item, character);

            if (character != null)
            {
                string msg = character.LogName + " launched " + item.Name + " (projectile: " + projectiles[0].Item.Name;
                if (projectiles[0].Item.ContainedItems == null || projectiles[0].Item.ContainedItems.All(i => i == null))
                {
                    msg += ")";
                }
                else
                {
                    msg += ", contained items: " + string.Join(", ", Array.FindAll(projectiles[0].Item.ContainedItems, i => i != null).Select(i => i.Name)) + ")";
                }
                GameServer.Log(msg, ServerLog.MessageType.ItemInteraction);
            }

            return true;
        }

        private void Launch(Item projectile, Character user = null)
        {
            reload = reloadTime;

            projectile.Drop();
            projectile.body.Dir = 1.0f;

            projectile.body.ResetDynamics();
            projectile.body.Enabled = true;
            projectile.SetTransform(ConvertUnits.ToSimUnits(new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y)), -rotation);
            projectile.FindHull();
            projectile.Submarine = projectile.body.Submarine;

            Projectile projectileComponent = projectile.GetComponent<Projectile>();
            if (projectileComponent != null)
            {
                projectileComponent.Use((float)Timing.Step);
                projectileComponent.User = user;
            }

            if (projectile.Container != null) projectile.Container.RemoveContained(projectile);

            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.components.IndexOf(this), projectile });
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f);
            LaunchProjSpecific();
        }

        partial void LaunchProjSpecific();

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (GetAvailablePower() < powerConsumption)
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
                    objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, "", false));                    
                    return false;
                }
            }

            int projectileCount = 0;
            int maxProjectileCount = 0;
            foreach (MapEntity e in item.linkedTo)
            {
                var projectileContainer = e as Item;
                if (projectileContainer == null) continue;
                
                var containedItems = projectileContainer.ContainedItems;
                if (containedItems != null)
                {
                    var container = projectileContainer.GetComponent<ItemContainer>();
                    if (containedItems != null) maxProjectileCount += container.Capacity;
                    projectileCount += containedItems.Length;
                }
            }

            if (projectileCount == 0 || (projectileCount < maxProjectileCount && objective.Option.ToLowerInvariant() != "fireatwill"))
            {
                ItemContainer container = null;
                foreach (MapEntity e in item.linkedTo)
                {
                    var containerItem = e as Item;
                    if (containerItem == null) continue;

                    container = containerItem.GetComponent<ItemContainer>();
                    if (container != null) break;
                }

                if (container == null || container.ContainableItems.Count == 0) return true;

                var containShellObjective = new AIObjectiveContainItem(character, container.ContainableItems[0].Identifiers[0], container);
                character?.Speak(TextManager.Get("DialogLoadTurret").Replace("[itemname]", item.Name), null, 0.0f, "loadturret", 30.0f);
                containShellObjective.MinContainedAmount = projectileCount + 1;
                containShellObjective.IgnoreAlreadyContainedItems = true;
                objective.AddSubObjective(containShellObjective);
                return false;
            }

            //enough shells and power
            Character closestEnemy = null;
            float closestDist = 3000.0f;
            foreach (Character enemy in Character.CharacterList)
            {
                //ignore humans and characters that are inside the sub
                if (enemy.IsDead || enemy.SpeciesName == "human" || enemy.AnimController.CurrentHull != null) continue;

                float dist = Vector2.Distance(enemy.WorldPosition, item.WorldPosition);
                if (dist < closestDist)
                {
                    closestEnemy = enemy;
                    closestDist = dist;
                }
            }

            if (closestEnemy == null) return false;

            character.CursorPosition = closestEnemy.WorldPosition;
            if (item.Submarine != null) character.CursorPosition -= item.Submarine.Position;
            character.SetInput(InputType.Aim, false, true);

            float enemyAngle = MathUtils.VectorToAngle(closestEnemy.WorldPosition - item.WorldPosition);
            float turretAngle = -rotation;

            if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.1f) return false;

            var pickedBody = Submarine.PickBody(ConvertUnits.ToSimUnits(item.WorldPosition), closestEnemy.SimPosition, null);
            if (pickedBody != null && !(pickedBody.UserData is Limb)) return false;

            if (objective.Option.ToLowerInvariant() == "fireatwill")
            {
                character?.Speak(TextManager.Get("DialogFireTurret").Replace("[itemname]", item.Name), null, 0.0f, "fireturret", 5.0f);
                character.SetInput(InputType.Use, true, true);
            }

            return false;
        }

        private float GetAvailablePower()
        {
            var batteries = item.GetConnectedComponents<PowerContainer>();

            float availablePower = 0.0f;
            foreach (PowerContainer battery in batteries)
            {
                float batteryPower = Math.Min(battery.Charge*3600.0f, battery.MaxOutPut);

                availablePower += batteryPower;
            }

            return availablePower;
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

            if (barrelSprite != null) barrelSprite.Remove();
            if (railSprite != null) railSprite.Remove();

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

            for (int i = 0; i < containedItems.Length; i++)
            {
                var projectileComponent = containedItems[i].GetComponent<Projectile>();
                if (projectileComponent != null)
                {
                    projectiles.Add(projectileComponent);
                    if (returnFirst) return;
                }
                else
                {
                    //check if the contained item is another itemcontainer with projectiles inside it
                    if (containedItems[i].ContainedItems == null) continue;
                    for (int j = 0; j < containedItems[i].ContainedItems.Length; j++)
                    {
                        projectileComponent = containedItems[i].ContainedItems[j].GetComponent<Projectile>();
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

            barrelPos.X = item.Rect.Width - barrelPos.X * item.Scale;

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
            minRotation = -minRotation;
            maxRotation = -maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            barrelPos.Y = item.Rect.Height - barrelPos.Y * item.Scale;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
            rotation = (minRotation + maxRotation) / 2;

            UpdateTransformedBarrelPos();
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
                    foreach (ItemComponent component in item.components)
                    {
                        if (component.Parent == this && component is LightComponent lightComponent)
                        {
                            lightComponent.IsOn = !lightComponent.IsOn;
                        }
                    }
                    break;
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            Item item = (Item)extraData[2];
            msg.Write(item.Removed ? (ushort)0 : item.ID);
        }
    }
}


