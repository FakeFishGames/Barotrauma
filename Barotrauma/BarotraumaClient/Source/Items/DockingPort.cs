using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (dockingState == 0.0f) return;

            Vector2 drawPos = item.DrawPosition;
            drawPos.Y = -drawPos.Y;

            var rect = overlaySprite.SourceRect;

            if (IsHorizontal)
            {
                drawPos.Y -= rect.Height / 2;

                if (DockingDir == 1)
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos,
                        new Rectangle(
                            rect.Center.X + (int)(rect.Width / 2 * (1.0f - dockingState)), rect.Y,
                            (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);

                }
                else
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos - Vector2.UnitX * (rect.Width / 2 * dockingState),
                        new Rectangle(
                            rect.X, rect.Y,
                            (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);
                }
            }
            else
            {
                drawPos.X -= rect.Width / 2;

                if (DockingDir == 1)
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos - Vector2.UnitY * (rect.Height / 2 * dockingState),
                        new Rectangle(
                            rect.X, rect.Y,
                            rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);
                }
                else
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos,
                        new Rectangle(
                            rect.X, rect.Y + rect.Height / 2 + (int)(rect.Height / 2 * (1.0f - dockingState)),
                            rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);
                }
            }

            if (!GameMain.DebugDraw) return;

            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    var body = bodies[i];
                    if (body == null) continue;

                    body.FixtureList[0].GetAABB(out AABB aabb, 0);

                    Vector2 bodyDrawPos = ConvertUnits.ToDisplayUnits(new Vector2(aabb.LowerBound.X, aabb.UpperBound.Y));
                    if ((i == 1 || i == 3) && DockingTarget?.item?.Submarine != null) bodyDrawPos += DockingTarget.item.Submarine.Position;
                    if ((i == 0 || i == 2) && item.Submarine != null) bodyDrawPos += item.Submarine.Position;
                    bodyDrawPos.Y = -bodyDrawPos.Y;

                    GUI.DrawRectangle(spriteBatch,
                        bodyDrawPos,
                        ConvertUnits.ToDisplayUnits(aabb.Extents * 2),
                        Color.Gray, false, 0.0f, 4);
                }
            }

            if (doorBody != null && doorBody.Enabled)
            {
                doorBody.FixtureList[0].GetAABB(out AABB aabb, 0);

                Vector2 bodyDrawPos = ConvertUnits.ToDisplayUnits(new Vector2(aabb.LowerBound.X, aabb.UpperBound.Y));
                if (item?.Submarine != null) bodyDrawPos += item.Submarine.Position;
                bodyDrawPos.Y = -bodyDrawPos.Y;

                GUI.DrawRectangle(spriteBatch,
                    bodyDrawPos,
                    ConvertUnits.ToDisplayUnits(aabb.Extents * 2),
                    Color.Gray, false, 0, 8);
            }
        }

    }
}
