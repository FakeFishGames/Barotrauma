using Barotrauma.Particles;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class RangedWeapon : ItemComponent
    {
        private Sprite crosshairSprite, crosshairPointerSprite;

        private Vector2 crosshairPos, crosshairPointerPos;

        private float currentCrossHairScale, currentCrossHairPointerScale;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();

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
                            crosshairSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        }
                        break;
                    case "crosshairpointer":
                        {
                            string texturePath = subElement.GetAttributeString("texture", "");
                            crosshairPointerSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        }
                        break;
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
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

                float degreeOfSuccess = DegreeOfSuccess(character);
                float spread = MathHelper.ToRadians(MathHelper.Lerp(UnskilledSpread, Spread, degreeOfSuccess));
                float crossHairDist = Vector2.Distance(item.WorldPosition, cam.ScreenToWorld(crosshairPos));
                float spreadDist = (float)Math.Sin(spread) * crossHairDist * 2.0f;

                currentCrossHairPointerScale = MathHelper.Clamp(spreadDist / Math.Min(crosshairSprite.size.X, crosshairSprite.size.Y), 0.1f, 10.0f);
            }
            currentCrossHairScale *= CrossHairScale;
            crosshairPointerPos = PlayerInput.MousePosition;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (crosshairSprite == null) { return; }
            if (character == null || !character.IsKeyDown(InputType.Aim)) { return; }

            GUI.HideCursor = (crosshairSprite != null || crosshairPointerSprite != null) &&
                GUI.MouseOn == null && !Inventory.IsMouseOnInventory() && !GameMain.Instance.Paused;
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
            crosshairSprite?.Remove();
            crosshairSprite = null;
            crosshairPointerSprite?.Remove();
            crosshairSprite = null;
        }
    }
}
