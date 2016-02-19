using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FarseerPhysics;

namespace Barotrauma.Items.Components
{
    class Turret : Powered
    {
        Sprite barrelSprite;

        Vector2 barrelPos;

        float rotation, targetRotation;

        float reload, reloadTime;

        float minRotation, maxRotation;

        float launchImpulse;

        Camera cam;

        [HasDefaultValue("0,0", false)]
        public string BarrelPos
        {
            get 
            { 
                return ToolBox.Vector2ToString(barrelPos); 
            }
            set 
            { 
                barrelPos = ToolBox.ParseToVector2(value); 
            }
        }

        [HasDefaultValue(0.0f, false)]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [HasDefaultValue(5.0f, false)]
        public float Reload
        {
            get { return reloadTime; }
            set { reloadTime = value; }
        }

        [HasDefaultValue("0.0,0.0", true), Editable]
        public string RotationLimits
        {
            get
            {
                Vector2 limits = new Vector2(minRotation, maxRotation);
                limits.X = MathHelper.ToDegrees(limits.X);
                limits.Y = MathHelper.ToDegrees(limits.Y);

                return ToolBox.Vector2ToString(limits); 
            }
            set
            {
                Vector2 vector = ToolBox.ParseToVector2(value);
                minRotation = MathHelper.ToRadians(vector.X);
                maxRotation = MathHelper.ToRadians(vector.Y);
            }
        }

        public Turret(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            barrelSprite = new Sprite(Path.GetDirectoryName(item.Prefab.ConfigFile) + "/" +element.Attribute("barrelsprite").Value,
                ToolBox.GetAttributeVector2(element, "origin", Vector2.Zero));
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            Vector2 drawPos = new Vector2(item.Rect.X, item.Rect.Y);
            if (item.Submarine != null) drawPos += item.Submarine.DrawPosition;
            drawPos.Y = -drawPos.Y;

            barrelSprite.Draw(spriteBatch, 
                 drawPos + barrelPos, Color.White,
                rotation + MathHelper.PiOver2, 1.0f, 
                SpriteEffects.None, item.Sprite.Depth+0.01f);

            if (!editing) return;

            GUI.DrawLine(spriteBatch, 
                drawPos + barrelPos, 
                drawPos + barrelPos + new Vector2((float)Math.Cos(minRotation), (float)Math.Sin(minRotation))*60.0f, 
                Color.Green);

            GUI.DrawLine(spriteBatch,
                drawPos + barrelPos,
                drawPos + barrelPos + new Vector2((float)Math.Cos(maxRotation), (float)Math.Sin(maxRotation)) * 60.0f,
                Color.Green);

            GUI.DrawLine(spriteBatch,
                drawPos + barrelPos,
                drawPos + barrelPos + new Vector2((float)Math.Cos((maxRotation + minRotation) / 2), (float)Math.Sin((maxRotation + minRotation) / 2)) * 60.0f,
                Color.LightGreen);

        }

        public override void Update(float deltaTime, Camera cam)
        {
            this.cam = cam;

            if (reload > 0.0f) reload -= deltaTime;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

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
            
            
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (reload > 0.0f) return false;

            var projectiles = GetLoadedProjectiles(true);
            if (projectiles.Count == 0) return false;

            if (GetAvailablePower() < powerConsumption) return false;
            
            var batteries = item.GetConnectedComponents<PowerContainer>();

            float availablePower = 0.0f;
            foreach (PowerContainer battery in batteries)
            {
                float batteryPower = Math.Min(battery.Charge*3600.0f, battery.MaxOutPut);
                float takePower = Math.Min(powerConsumption - availablePower, batteryPower);

                battery.Charge -= takePower/3600.0f;
            }

            reload = reloadTime;            

            Item projectile = projectiles[0].Item;

            projectile.body.ResetDynamics();
            projectile.body.Enabled = true;
            projectile.SetTransform(ConvertUnits.ToSimUnits(new Vector2(item.WorldRect.X + barrelPos.X, item.WorldRect.Y - barrelPos.Y)), -rotation);
            projectile.FindHull();
            projectile.Submarine = projectile.body.Submarine;

            projectiles[0].Use(deltaTime);
            projectiles[0].User = character;

            if (projectile.Container != null) projectile.Container.RemoveContained(projectile);

            return true;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            var projectiles = GetLoadedProjectiles();

            if (projectiles.Count==0 || (projectiles.Count==1 && objective.Option.ToLower()!="fire at will"))
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

                if (batteryToLoad.RechargeSpeed < batteryToLoad.MaxRechargeSpeed*0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, ""));
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
            //Vector2 receive

            //Vector2 centerPos = new Vector2(item.WorldRect.X + barrelPos.X, item.WorldRect.Y - barrelPos.Y);

            //Vector2 offset = receivedPos - centerPos;
            //offset.Y = -offset.Y;

            //targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));

            float enemyAngle = MathUtils.VectorToAngle(closestEnemy.WorldPosition-item.WorldPosition);
            float turretAngle = -rotation;



            if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.01f) return false;

            var pickedBody = Submarine.PickBody(ConvertUnits.ToSimUnits(item.WorldPosition), closestEnemy.SimPosition, null);
            if (pickedBody != null && pickedBody.UserData as Limb == null) return false;

            if (objective.Option.ToLower()=="fire at will") Use(deltaTime, character);


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

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            barrelSprite.Remove();
        }

        private List<Projectile> GetLoadedProjectiles(bool returnFirst = false)
        {
            List<Projectile> projectiles = new List<Projectile>();

            foreach (MapEntity e in item.linkedTo)
            {
                var projectileContainer = e as Item;
                if (projectileContainer == null) continue;

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

            return projectiles;
        }
        
        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power)
        {
            switch (connection.Name)
            {
                case "position_in":
                    Vector2 receivedPos = ToolBox.ParseToVector2(signal, false);

                    Vector2 centerPos = new Vector2(item.WorldRect.X + barrelPos.X, item.WorldRect.Y - barrelPos.Y);

                    Vector2 offset = receivedPos - centerPos;
                    offset.Y = -offset.Y;
                   
                    targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));

                    IsActive = true;

                    break;
                case "trigger_in":
                    item.Use((float)Physics.step, null);
                    break;
            }
        }
    }
}


