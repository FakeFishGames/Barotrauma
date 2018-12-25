using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite crosshairSprite, disabledCrossHairSprite;

        private GUIProgressBar powerIndicator;

        [Editable, Serialize("0.0,0.0,0.0,0.0", true)]
        public Color HudTint
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool ShowChargeIndicator
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool ShowProjectileIndicator
        {
            get;
            private set;
        }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                string texturePath = subElement.GetAttributeString("texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crosshair":
                        crosshairSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "disabledcrosshair":
                        disabledCrossHairSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                }
            }

            int barWidth = 200;
            powerIndicator = new GUIProgressBar(new Rectangle(GameMain.GraphicsWidth / 2 - barWidth / 2, 20, barWidth, 30), Color.White, 0.0f);
        }

        partial void LaunchProjSpecific()
        {
            PlaySound(ActionType.OnUse, item.WorldPosition);
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            Vector2 drawPos = new Vector2(item.Rect.X, item.Rect.Y);
            if (item.Submarine != null) drawPos += item.Submarine.DrawPosition;
            drawPos.Y = -drawPos.Y;

            if (barrelSprite != null)
            {
                barrelSprite.Draw(spriteBatch,
                     drawPos + barrelPos, Color.White,
                    rotation + MathHelper.PiOver2, 1.0f,
                    SpriteEffects.None, item.Sprite.Depth + 0.01f);
            }

            if (!editing) return;

            GUI.DrawLine(spriteBatch,
                drawPos + barrelPos,
                drawPos + barrelPos + new Vector2((float)Math.Cos(minRotation), (float)Math.Sin(minRotation)) * 60.0f,
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

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (HudTint.A > 0)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), 
                    new Color(HudTint.R, HudTint.G, HudTint.B) * (HudTint.A/255.0f), true);
            }

            float batteryCharge;
            float batteryCapacity;
            GetAvailablePower(out batteryCharge, out batteryCapacity);

            List<Projectile> projectiles = GetLoadedProjectiles(false, true);

            float chargeRate = powerConsumption <= 0.0f ? 1.0f : batteryCharge / batteryCapacity;
            bool charged = batteryCharge * 3600.0f > powerConsumption;
            bool readyToFire = reload <= 0.0f && charged && projectiles.Any(p => p != null);

            if (ShowChargeIndicator && PowerConsumption > 0.0f)
            {
                powerIndicator.BarSize = chargeRate;
                powerIndicator.Color = charged ? Color.Green : Color.Red;
                powerIndicator.Draw(spriteBatch);

                int requiredChargeIndicatorPos = (int)((powerConsumption / (batteryCapacity * 3600.0f)) * powerIndicator.Rect.Width);
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(powerIndicator.Rect.X + requiredChargeIndicatorPos, powerIndicator.Rect.Y, 3, powerIndicator.Rect.Height),
                    Color.White * 0.5f, true);
            }

            if (ShowProjectileIndicator)
            {
                Point slotSize = new Point(60, 30);
                int spacing = 5;
                int slotsPerRow = Math.Min(projectiles.Count, 6);
                int totalWidth = slotSize.X * slotsPerRow + spacing * (slotsPerRow - 1);
                Point invSlotPos = new Point(GameMain.GraphicsWidth / 2 - totalWidth / 2, 60);
                for (int i = 0; i < projectiles.Count; i++)
                {
                    Inventory.DrawSlot(spriteBatch,
                        new InventorySlot(new Rectangle(invSlotPos + new Point((i % slotsPerRow) * (slotSize.X + spacing), (int)Math.Floor(i / (float)slotsPerRow) * (slotSize.Y + spacing)), slotSize)),
                        projectiles[i] == null ? null : projectiles[i].Item, true);
                }
            }

            if (readyToFire)
            {
                if (crosshairSprite != null) crosshairSprite.Draw(spriteBatch, PlayerInput.MousePosition);
            }
            else
            {
                if (disabledCrossHairSprite != null) disabledCrossHairSprite.Draw(spriteBatch, PlayerInput.MousePosition);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            UInt16 projectileID = msg.ReadUInt16();
            //projectile removed, do nothing
            if (projectileID == 0) return;

            Item projectile = Entity.FindEntityByID(projectileID) as Item;
            if (projectile == null)
            {
                DebugConsole.ThrowError("Failed to launch a projectile - item with the ID \"" + projectileID + " not found");
                return;
            }

            Launch(projectile);
        }
    }
}
