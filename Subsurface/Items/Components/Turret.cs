using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Items.Components
{
    class Turret : Powered
    {
        Sprite barrelSprite;

        Vector2 barrelPos;

        float targetRotation;
        float rotation;

        float reload;
        float reloadTime;

        float minRotation;
        float maxRotation;

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

        [HasDefaultValue("0.0,0.0", false)]
        public string RotationLimits
        {
            get
            {
                return ToolBox.Vector2ToString(barrelPos); 
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
            isActive = true;

            barrelSprite = new Sprite(Path.GetDirectoryName(item.Prefab.ConfigFile) + "\\" +element.Attribute("barrelsprite").Value,
                ToolBox.GetAttributeVector2(element, "origin", Vector2.Zero));

            //barrelPos = ToolBox.GetAttributeVector2(element, "BarrelPos", Vector2.Zero);

            //launchImpulse = ToolBox.GetAttributeFloat(element, "launchimpulse", 0.0f);

            //minRotation = ToolBox.GetAttributeFloat(element, "MinimumRotation", 0.0f);
            //maxRotation = ToolBox.GetAttributeFloat(element, "MaximumRotation", MathHelper.TwoPi);

            //reloadTime = ToolBox.GetAttributeFloat(element, "reload", 5.0f);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            barrelSprite.Draw(spriteBatch, new Vector2(item.Rect.X, -item.Rect.Y) + barrelPos, rotation + MathHelper.PiOver2, 1.0f);

            //GUI.DrawRectangle(spriteBatch,
            //    new Rectangle((int)(rect.X + barrelPos.X), (int)(-rect.Y + barrelPos.Y), 10, 10),
            //    Color.White, true);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            //if (character == null || character.SelectedConstruction != item)
            //{
            //    character = null;
            //    isActive = false;
            //    return;
            //}

            this.cam = cam;

            if (reload>0.0f) reload -= deltaTime;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (targetRotation < minRotation || targetRotation > maxRotation)
            {
                float diff = MathUtils.WrapAngleTwoPi(targetRotation - (minRotation + maxRotation) / 2.0f);
                targetRotation = (diff > Math.PI) ? minRotation : maxRotation;
            }
            
            rotation = MathUtils.CurveAngle(rotation, targetRotation, 0.05f);
            
            //if (!prefab.FocusOnSelected) return;

            //cam.OffsetAmount = prefab.OffsetOnSelected;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (character == null) return;
            Vector2 centerPos = new Vector2(item.Rect.X + barrelPos.X, item.Rect.Y - barrelPos.Y);
            
            Vector2 offset = character.CursorPosition - centerPos;
            offset.Y = -offset.Y;
                   
            targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));

            isActive = true;

            if (character == Character.Controlled && cam!=null)
            {
                Lights.LightManager.viewPos = centerPos;
                cam.TargetPos = new Vector2(item.Rect.X + barrelPos.X, item.Rect.Y - barrelPos.Y);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (reload > 0.0f) return false;

            Projectile projectileComponent = null;

            currPowerConsumption = powerConsumption;

            float availablePower = 0.0f;
            //List<PowerContainer> batteries = new List<PowerContainer>();
            foreach (MapEntity e in item.linkedTo)
            {
                Item battery = e as Item;
                if (battery == null) continue;

                PowerContainer batteryComponent = battery.GetComponent<PowerContainer>();
                if (batteryComponent == null) continue;

                float batteryPower = Math.Min(batteryComponent.Charge, batteryComponent.MaxOutPut);
                float takePower = Math.Min(currPowerConsumption - availablePower, batteryPower);

                batteryComponent.Charge -= takePower;
                availablePower += takePower;
            }

            reload = reloadTime;
            
            if (availablePower < currPowerConsumption) return false;
            
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

            if (projectile == null || projectileComponent==null) return false;

            projectile.body.ResetDynamics();
            projectile.body.Enabled = true;
            projectile.SetTransform(item.SimPosition, rotation);

            //if (useSounds.Count() > 0) useSounds[Game1.localRandom.Next(useSounds.Count())].Play(1.0f, 800.0f, item.body.FarseerBody);

            projectileComponent.Use(deltaTime);
            item.RemoveContained(projectile);

            return true;
        }
    }
}


