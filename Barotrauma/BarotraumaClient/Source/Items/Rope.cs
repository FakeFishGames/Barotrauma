using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent, IDrawableComponent
    {
        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (!IsActive) return;

            RevoluteJoint firstJoint = null;

            for (int i = 0; i < ropeBodies.Length - 1; i++)
            {
                if (!ropeBodies[i].Enabled) continue;

                if (firstJoint == null) firstJoint = ropeJoints[i];

                DrawSection(spriteBatch, ropeJoints[i].WorldAnchorA, ropeJoints[i + 1].WorldAnchorA, i);
            }

            if (gunJoint == null || firstJoint == null) return;

            DrawSection(spriteBatch, gunJoint.WorldAnchorA, firstJoint.WorldAnchorA, 0);

        }

        private void DrawSection(SpriteBatch spriteBatch, Vector2 start, Vector2 end, int i)
        {
            start.Y = -start.Y;
            end.Y = -end.Y;

            spriteBatch.Draw(sprite.Texture,
                ConvertUnits.ToDisplayUnits(start), null, Color.White,
                MathUtils.VectorToAngle(end - start),
                new Vector2(0.0f, sprite.size.Y / 2.0f),
                new Vector2((ConvertUnits.ToDisplayUnits(Vector2.Distance(start, end))) / sprite.Texture.Width, 1.0f),
                SpriteEffects.None,
                sprite.Depth + i * 0.00001f);
        }
    }
}
