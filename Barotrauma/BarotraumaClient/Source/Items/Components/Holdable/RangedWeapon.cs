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
        private Sprite crosshairSprite;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crosshair":
                        string texturePath = subElement.GetAttributeString("texture", "");
                        crosshairSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                }
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //TODO: draw crosshair
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
        }
    }
}
