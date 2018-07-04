using Barotrauma.Networking;
using Barotrauma.Sounds;
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
        private Sprite crosshairSprite, crosshairPointerSprite;

        private GUIProgressBar powerIndicator;

        private float recoilTimer;

        private Sound startMoveSound, endMoveSound, moveSound;

        private SoundChannel moveSoundChannel;

        private Vector2 crosshairPos, crosshairPointerPos;

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

        [Serialize(0.0f, false)]
        public float RecoilDistance
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
                    case "crosshairpointer":
                        crosshairPointerSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "startmovesound":
                        startMoveSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                    case "endmovesound":
                        endMoveSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                    case "movesound":
                        moveSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                }
            }
            
            powerIndicator = new GUIProgressBar(new RectTransform(new Vector2(0.18f, 0.03f), GUI.Canvas, Anchor.TopCenter)
            {
                MinSize = new Point(100,20),
                RelativeOffset = new Vector2(0.0f, 0.01f)
            }, 
            barSize: 0.0f);
        }

        partial void LaunchProjSpecific()
        {
            recoilTimer = Math.Max(Reload, 0.1f);
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            recoilTimer -= deltaTime;

            if (crosshairSprite != null)
            {
                Vector2 itemPos = cam.WorldToScreen(item.WorldPosition);
                Vector2 turretDir = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + turretDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + turretDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));
            }

            crosshairPointerPos = PlayerInput.MousePosition;

            if (Math.Abs(angularVelocity) > 0.1f)
            {
                if (moveSoundChannel == null && startMoveSound != null)
                {
                    moveSoundChannel = SoundPlayer.PlaySound(startMoveSound, 1.0f, startMoveSound.BaseFar, item.WorldPosition);
                }
                else if (moveSoundChannel == null || !moveSoundChannel.IsPlaying)
                {
                    if (moveSound != null)
                    {
                        moveSoundChannel.Dispose();
                        moveSoundChannel = SoundPlayer.PlaySound(moveSound, 1.0f, moveSound.BaseFar, item.WorldPosition);
                        if (moveSoundChannel != null) moveSoundChannel.Looping = true;
                    }
                }
            }
            else if (Math.Abs(angularVelocity) < 0.05f)
            {
                if (moveSoundChannel != null)
                {
                    if (endMoveSound != null && moveSoundChannel.Sound != endMoveSound)
                    {
                        moveSoundChannel.Dispose();
                        moveSoundChannel = SoundPlayer.PlaySound(endMoveSound, 1.0f, endMoveSound.BaseFar, item.WorldPosition);
                        if (moveSoundChannel != null) moveSoundChannel.Looping = false;
                    }
                    else if (!moveSoundChannel.IsPlaying)
                    {
                        moveSoundChannel.Dispose();
                        moveSoundChannel = null;

                    }
                }
            }

            if (moveSoundChannel != null && moveSoundChannel.IsPlaying)
            {
                moveSoundChannel.Gain = MathHelper.Clamp(Math.Abs(angularVelocity), 0.5f, 1.0f);
            }
        }

        public override void UpdateHUD(Character character, float deltaTime)
        {
            if (crosshairSprite != null)
            {
                Vector2 itemPos = cam.WorldToScreen(item.WorldPosition);
                Vector2 turretDir = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + turretDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + turretDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));
            }

            crosshairPointerPos = PlayerInput.MousePosition;
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            Vector2 drawPos = new Vector2(item.Rect.X, item.Rect.Y);
            if (item.Submarine != null) drawPos += item.Submarine.DrawPosition;
            drawPos.Y = -drawPos.Y;

            float recoilOffset = 0.0f;
            if (RecoilDistance > 0.0f && recoilTimer > 0.0f)
            {
                //move the barrel backwards 0.1 seconds after launching
                if (recoilTimer >= Math.Max(Reload, 0.1f) - 0.1f)
                {
                    recoilOffset = RecoilDistance * (1.0f - (recoilTimer - (Math.Max(Reload, 0.1f) - 0.1f)) / 0.1f);
                }
                //move back to normal position while reloading
                else
                {
                    recoilOffset = RecoilDistance * recoilTimer / (Math.Max(Reload, 0.1f) - 0.1f);
                }
            }

            if (barrelSprite != null)
            {
                barrelSprite.Draw(spriteBatch,
                    drawPos + barrelPos - new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation)) * recoilOffset, 
                    Color.White,
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
                    new Color(HudTint.R, HudTint.G, HudTint.B) * (HudTint.A / 255.0f), true);
            }

            GetAvailablePower(out float batteryCharge, out float batteryCapacity);

            List<Item> availableAmmo = new List<Item>();
            foreach (MapEntity e in item.linkedTo)
            {
                var linkedItem = e as Item;
                if (linkedItem == null) continue;

                var itemContainer = linkedItem.GetComponent<ItemContainer>();
                if (itemContainer?.Inventory?.Items == null) continue;   
                
                availableAmmo.AddRange(itemContainer.Inventory.Items);                
            }            

            float chargeRate = powerConsumption <= 0.0f ? 1.0f : batteryCharge / batteryCapacity;
            bool charged = batteryCharge * 3600.0f > powerConsumption;
            bool readyToFire = reload <= 0.0f && charged && availableAmmo.Any(p => p != null);

            if (ShowChargeIndicator && PowerConsumption > 0.0f)
            {
                powerIndicator.BarSize = chargeRate;
                powerIndicator.Color = charged ? Color.Green : Color.Red;
                powerIndicator.DrawManually(spriteBatch, true);

                int requiredChargeIndicatorPos = (int)((powerConsumption / (batteryCapacity * 3600.0f)) * powerIndicator.Rect.Width);
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(powerIndicator.Rect.X + requiredChargeIndicatorPos, powerIndicator.Rect.Y, 3, powerIndicator.Rect.Height),
                    Color.White * 0.5f, true);
            }

            if (ShowProjectileIndicator)
            {
                Point slotSize = new Point(60, 30);
                int spacing = 5;
                int slotsPerRow = Math.Min(availableAmmo.Count, 6);
                int totalWidth = slotSize.X * slotsPerRow + spacing * (slotsPerRow - 1);
                Point invSlotPos = new Point(GameMain.GraphicsWidth / 2 - totalWidth / 2, 60);
                for (int i = 0; i < availableAmmo.Count; i++)
                {
                    Inventory.DrawSlot(spriteBatch, null,
                        new InventorySlot(new Rectangle(invSlotPos + new Point((i % slotsPerRow) * (slotSize.X + spacing), (int)Math.Floor(i / (float)slotsPerRow) * (slotSize.Y + spacing)), slotSize)),
                        availableAmmo[i], true);
                }
            }

            if (crosshairSprite != null)
            {
                crosshairSprite.Draw(spriteBatch, crosshairPos, readyToFire ? Color.White : Color.White * 0.2f, 0, (float)Math.Sqrt(cam.Zoom));
            }
            if (crosshairPointerSprite != null) crosshairPointerSprite.Draw(spriteBatch, crosshairPointerPos, 0, (float)Math.Sqrt(cam.Zoom));            
        }
        
        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            UInt16 projectileID = msg.ReadUInt16();
            Item projectile = Entity.FindEntityByID(projectileID) as Item;

            if (projectile == null)
            {
                DebugConsole.ThrowError("Failed to launch a projectile - item with the ID \"" + projectileID + " not found");
                return;
            }

            Launch(projectile);
            PlaySound(ActionType.OnUse, item.WorldPosition);
        }
    }
}
