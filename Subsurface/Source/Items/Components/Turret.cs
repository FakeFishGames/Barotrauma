using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FarseerPhysics;

namespace Subsurface.Items.Components
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
            barrelSprite.Draw(spriteBatch, 
                new Vector2(item.Rect.X, -item.Rect.Y) + barrelPos, Color.White,
                rotation + MathHelper.PiOver2, 1.0f, 
                SpriteEffects.None, item.Sprite.Depth+0.01f);
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

            Projectile projectileComponent = null;
            //search for a projectile from linked containers
            Item projectile = null;
            foreach (MapEntity e in item.linkedTo)
            {
                Item container = e as Item;
                if (container == null) continue;

                ItemContainer containerComponent = container.GetComponent<ItemContainer>();
                if (containerComponent == null) continue;

                for (int i = 0; i < containerComponent.inventory.items.Length; i++)
                {
                    if (containerComponent.inventory.items[i] == null) continue;
                    if ((projectileComponent = containerComponent.inventory.items[i].GetComponent<Projectile>()) != null)
                    {
                        projectile = containerComponent.inventory.items[i];
                        break;
                    }
                }

                if (projectileComponent != null) break;
            }

            if (projectile == null || projectileComponent == null) return false;


            float availablePower = 0.0f;
            //List<PowerContainer> batteries = new List<PowerContainer>();
            foreach (Connection c in item.Connections)
            {
                foreach (Connection c2 in c.Recipients)
                {
                    if (c2 == null || c2.Item == null) continue;

                    PowerContainer batteryComponent = c2.Item.GetComponent<PowerContainer>();
                    if (batteryComponent == null) continue;

                    float batteryPower = Math.Min(batteryComponent.Charge, batteryComponent.MaxOutPut);
                    float takePower = Math.Min(currPowerConsumption - availablePower, batteryPower);

                    batteryComponent.Charge -= takePower;
                    availablePower += takePower;
                }
            }

            reload = reloadTime;
            
            if (availablePower < currPowerConsumption) return false;
            
            projectile.body.ResetDynamics();
            projectile.body.Enabled = true;
            projectile.SetTransform(ConvertUnits.ToSimUnits(new Vector2(item.Rect.X + barrelPos.X, item.Rect.Y - barrelPos.Y)), -rotation);

            projectileComponent.Use(deltaTime);
            item.RemoveContained(projectile);

            return true;
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power)
        {
            switch (connection.Name)
            {
                case "position_in":
                    Vector2 receivedPos = ToolBox.ParseToVector2(signal, false);

                    Vector2 centerPos = new Vector2(item.Rect.X + barrelPos.X, item.Rect.Y - barrelPos.Y);

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


