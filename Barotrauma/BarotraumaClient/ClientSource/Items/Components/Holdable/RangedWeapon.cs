using Barotrauma.Particles;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Text;
using System.Xml.Linq;
using Barotrauma.Sounds;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class RangedWeapon : ItemComponent
    {
        protected Sprite crosshairSprite, crosshairPointerSprite;

        protected Vector2 crosshairPos, crosshairPointerPos;

        protected float currentCrossHairScale, currentCrossHairPointerScale;

        private RoundSound chargeSound;
        private SoundChannel chargeSoundChannel;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();
        private readonly List<ParticleEmitter> particleEmitterCharges = new List<ParticleEmitter>();

        [Serialize(1.0f, false, description: "The scale of the crosshair sprite (if there is one).")]
        public float CrossHairScale
        {
            get;
            private set;
        }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crosshair":
                        {
                            string texturePath = subElement.GetAttributeString("texture", "");
                            crosshairSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.FilePath));
                        }
                        break;
                    case "crosshairpointer":
                        {
                            string texturePath = subElement.GetAttributeString("texture", "");
                            crosshairPointerSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.FilePath));
                        }
                        break;
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                    case "particleemittercharge":
                        particleEmitterCharges.Add(new ParticleEmitter(subElement));
                        break;
                    case "chargesound":
                        chargeSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                }
            }
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            currentCrossHairScale = currentCrossHairPointerScale = cam == null ? 1.0f : cam.Zoom;
            if (crosshairSprite != null)
            {
                Vector2 aimRefWorldPos = character.AimRefPosition;
                if (character.Submarine != null) { aimRefWorldPos += character.Submarine.Position; }
                Vector2 itemPos = cam.WorldToScreen(aimRefWorldPos);
                float rotation = (item.body.Dir == 1.0f) ? item.body.Rotation : item.body.Rotation - MathHelper.Pi;
                Vector2 barrelDir = new Vector2((float)Math.Cos(rotation), -(float)Math.Sin(rotation));

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + barrelDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + barrelDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));

                float spread = GetSpread(character);
                Projectile projectile = FindProjectile();
                if (projectile != null)
                {
                    spread += MathHelper.ToRadians(projectile.Spread);
                }

                float crossHairDist = Vector2.Distance(item.WorldPosition, cam.ScreenToWorld(crosshairPos));
                float spreadDist = (float)Math.Sin(spread) * crossHairDist;

                currentCrossHairPointerScale = MathHelper.Clamp(spreadDist / Math.Min(crosshairSprite.size.X, crosshairSprite.size.Y), 0.1f, 10.0f);
            }
            currentCrossHairScale *= CrossHairScale;
            crosshairPointerPos = PlayerInput.MousePosition;
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            float chargeRatio = currentChargeTime / MaxChargeTime;

            switch (currentChargingState)
            {
                case ChargingState.WindingUp:
                case ChargingState.WindingDown:
                    Vector2 particlePos = item.WorldPosition + ConvertUnits.ToDisplayUnits(TransformedBarrelPos);
                    float sizeMultiplier = Math.Clamp(chargeRatio, 0.1f, 1f);
                    foreach (ParticleEmitter emitter in particleEmitterCharges)
                    {
                        emitter.Emit(deltaTime, particlePos, hullGuess: null, sizeMultiplier: sizeMultiplier, colorMultiplier: emitter.Prefab.Properties.ColorMultiplier);
                    }

                    if (chargeSoundChannel == null || !chargeSoundChannel.IsPlaying)
                    {
                        if (chargeSound != null)
                        {
                            chargeSoundChannel = SoundPlayer.PlaySound(chargeSound.Sound, item.WorldPosition, chargeSound.Volume, chargeSound.Range, ignoreMuffling: chargeSound.IgnoreMuffling);
                            if (chargeSoundChannel != null) chargeSoundChannel.Looping = true;
                        }
                    }
                    else if (chargeSoundChannel != null)
                    {
                        chargeSoundChannel.FrequencyMultiplier = MathHelper.Lerp(0.5f, 1.5f, chargeRatio);
                    }
                    break;
                default:
                    if (chargeSoundChannel != null)
                    {
                        if (chargeSoundChannel.IsPlaying)
                        {
                            chargeSoundChannel.FadeOutAndDispose();
                            chargeSoundChannel.Looping = false;
                        }
                        else
                        {
                            chargeSoundChannel = null;
                        }
                    }
                    break;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character == null || !character.IsKeyDown(InputType.Aim)) { return; }

            //camera focused on some other item/device, don't draw the crosshair
            if (character.ViewTarget != null && (character.ViewTarget is Item viewTargetItem) && viewTargetItem.Prefab.FocusOnSelected) { return; }
            //don't draw the crosshair if the item is in some other type of equip slot than hands (e.g. assault rifle in the bag slot)
            if (!character.HeldItems.Contains(item)) { return; }

            GUI.HideCursor = (crosshairSprite != null || crosshairPointerSprite != null) &&
                GUI.MouseOn == null && !Inventory.IsMouseOnInventory && !GameMain.Instance.Paused;
            if (GUI.HideCursor)
            {
                crosshairSprite?.Draw(spriteBatch, crosshairPos, Color.White, 0, currentCrossHairScale);
                crosshairPointerSprite?.Draw(spriteBatch, crosshairPointerPos, 0, currentCrossHairPointerScale);
            }
        }

        partial void LaunchProjSpecific()
        {
            Vector2 particlePos = item.WorldPosition + ConvertUnits.ToDisplayUnits(TransformedBarrelPos);
            float rotation = -item.body.Rotation;
			if (item.body.Dir < 0.0f) { rotation += MathHelper.Pi; }
            foreach (ParticleEmitter emitter in particleEmitters)
            {
                emitter.Emit(1.0f, particlePos, hullGuess: null, angle: rotation, particleRotation: rotation);
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            crosshairSprite?.Remove();
            crosshairSprite = null;
            crosshairPointerSprite?.Remove();
            crosshairSprite = null;
        }
    }
}
