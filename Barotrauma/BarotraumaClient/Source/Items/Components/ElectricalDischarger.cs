using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class ElectricalDischarger : ItemComponent, IDrawableComponent
    {
        private static SpriteSheet electricitySprite;

        private int frameOffset;

        partial void InitProjSpecific()
        {
            if (electricitySprite == null)
            {
                electricitySprite = new SpriteSheet("Content/Lights/Electricity.png", 4, 4, new Vector2(0.5f, 0.0f));
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            IsActive = true;
            DrawElectricity(spriteBatch);
        }

        public void DrawElectricity(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Length <= 1.0f) continue;
                var node = nodes[i];
                electricitySprite.Draw(spriteBatch,
                    (i + frameOffset) % electricitySprite.FrameCount,
                    new Vector2(node.WorldPosition.X, -node.WorldPosition.Y),
                    Color.Lerp(Color.LightBlue, Color.White, Rand.Range(0.0f, 1.0f)) * Rand.Range(0.5f, 1.0f),
                    electricitySprite.Origin, -node.Angle - MathHelper.PiOver2,
                    new Vector2(Math.Min(node.Length / electricitySprite.FrameSize.X, 2.0f), node.Length / electricitySprite.FrameSize.Y));
            }
        }
    }
}
