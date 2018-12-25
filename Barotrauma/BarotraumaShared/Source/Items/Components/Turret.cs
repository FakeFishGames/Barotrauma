using Barotrauma.Networking;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        Sprite barrelSprite;

        Vector2 barrelPos;

        bool? hasLight;
        LightComponent lightComponent;

        float rotation, targetRotation;

        float reload, reloadTime;

        float minRotation, maxRotation;

        float launchImpulse;

        Camera cam;

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
            }
        }

        [Serialize(0.0f, false)]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [Serialize(5.0f, false)]
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
        
        public Turret(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            string barrelSpritePath = element.GetAttributeString("barrelsprite", "");

            if (!string.IsNullOrWhiteSpace(barrelSpritePath))
            {
                if (!barrelSpritePath.Contains("/"))
                {
                    barrelSpritePath = Path.Combine(Path.GetDirectoryName(item.Prefab.ConfigFile), barrelSpritePath);
                }

                barrelSprite = new Sprite(
                    barrelSpritePath,
                    element.GetAttributeVector2("origin", Vector2.Zero));
            }

            hasLight = null;

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            if (hasLight == null)
            {
                List<LightComponent> lightComponents = item.GetComponents<LightComponent>();
                
                if (lightComponents != null && lightComponents.Count>0)
                {
                    lightComponent = lightComponents.Find(lc => lc.Parent == this);
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

            if (minRotation == maxRotation) return;

            float targetMidDiff = MathHelper.WrapAngle(targetRotation - (minRotation + maxRotation) / 2.0f);

            float maxDist = (maxRotation - minRotation) / 2.0f;

            if (Math.Abs(targetMidDiff) > maxDist)
            {
                targetRotation = (targetMidDiff < 0.0f) ? minRotation : maxRotation;
            }

            float deltaRotation = MathHelper.WrapAngle(targetRotation-rotation);
            deltaRotation = MathHelper.Clamp(deltaRotation, -0.5f, 0.5f) * 5.0f;

            rotation += deltaRotation * deltaTime;

            float rotMidDiff = MathHelper.WrapAngle(rotation - (minRotation + maxRotation) / 2.0f);

            if (rotMidDiff < -maxDist)
            {
                rotation = minRotation;
            } 
            else if (rotMidDiff > maxDist)
            {
                rotation = maxRotation;
            }

            if ((bool)hasLight)
            {
                lightComponent.Rotation = rotation;
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!characterUsable && character != null) return false;
            return TryLaunch(character);
        }

        private bool TryLaunch(Character character = null)
        {
            if (GameMain.Client != null) return false;

            if (reload > 0.0f) return false;

            var projectiles = GetLoadedProjectiles(true);
            if (projectiles.Count == 0) return false;

            if (GetAvailablePower() < powerConsumption) return false;

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
            projectile.SetTransform(ConvertUnits.ToSimUnits(new Vector2(item.WorldRect.X + barrelPos.X, item.WorldRect.Y - barrelPos.Y)), -rotation);
            projectile.FindHull();
            projectile.Submarine = projectile.body.Submarine;

            LaunchProjSpecific();

            ApplyStatusEffects(ActionType.OnUse, 1.0f, user);

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
        }

        partial void LaunchProjSpecific();

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            var projectiles = GetLoadedProjectiles();

            if (projectiles.Count == 0 || (projectiles.Count == 1 && objective.Option.ToLowerInvariant() != "fire at will"))
            {
                ItemContainer container = null;
                foreach (MapEntity e in item.linkedTo)
                {
                    var containerItem = e as Item;
                    if (containerItem == null) continue;

                    container = containerItem.GetComponent<ItemContainer>();
                    if (container != null) break;
                }

                if (container == null || container.ContainableItems.Count==0) return true;

                var containShellObjective = new AIObjectiveContainItem(character, container.ContainableItems[0].Names[0], container);
                containShellObjective.IgnoreAlreadyContainedItems = true;
                objective.AddSubObjective(containShellObjective);
                return false;
            }
            else if (GetAvailablePower() < powerConsumption)
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
            if (item.Submarine!=null) character.CursorPosition -= item.Submarine.Position;
            character.SetInput(InputType.Aim, false, true);

            float enemyAngle = MathUtils.VectorToAngle(closestEnemy.WorldPosition-item.WorldPosition);
            float turretAngle = -rotation;

            if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.01f) return false;

            var pickedBody = Submarine.PickBody(ConvertUnits.ToSimUnits(item.WorldPosition), closestEnemy.SimPosition, null);
            if (pickedBody != null && !(pickedBody.UserData is Limb)) return false;

            if (objective.Option.ToLowerInvariant() == "fire at will") Use(deltaTime, character);

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
        }

        private List<Projectile> GetLoadedProjectiles(bool returnFirst = false, bool returnNull = false)
        {
            List<Projectile> projectiles = new List<Projectile>();

            foreach (MapEntity e in item.linkedTo)
            {
                var projectileContainer = e as Item;
                if (projectileContainer == null) continue;

                if (returnNull)
                {
                    var itemContainer = projectileContainer.GetComponent<ItemContainer>();
                    if (itemContainer == null) continue;
                    if (itemContainer.Inventory == null) continue;
                    if (itemContainer.Inventory.Items == null) continue;
                    for (int i = 0; i < itemContainer.Inventory.Items.Length; i++)
                    {
                        projectiles.Add(itemContainer.Inventory.Items[i]?.GetComponent<Projectile>());                        
                    }
                }
                else
                {
                    var containedItems = projectileContainer.ContainedItems;
                    if (containedItems == null) continue;

                    for (int i = 0; i < containedItems.Length; i++)
                    {
                        var projectileComponent = containedItems[i].GetComponent<Projectile>();
                        if (projectileComponent != null)
                        {
                            projectiles.Add(projectileComponent);
                            if (returnFirst) return projectiles;
                        }
                    }
                }
            }

            return projectiles;
        }

        public override void FlipX()
        {
            minRotation = (float)Math.PI - minRotation;
            maxRotation = (float)Math.PI - maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power)
        {
            switch (connection.Name)
            {
                case "position_in":
                    float.TryParse(signal, out targetRotation);

                    IsActive = true;

                    break;
                case "trigger_in":
                    item.Use((float)Timing.Step, sender);
                    //triggering the Use method through item.Use will fail if the item is not characterusable and the signal was sent by a character
                    //so lets do it manually
                    if (!characterUsable && sender != null)
                    {
                        TryLaunch(sender);
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


