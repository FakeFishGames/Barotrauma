using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class Gap : MapEntity
    {
        public override void Draw(SpriteBatch sb, bool editing, bool back = true)
        {
            if (GameMain.DebugDraw)
            {
                Vector2 center = new Vector2(WorldRect.X + rect.Width / 2.0f, -(WorldRect.Y - rect.Height / 2.0f));

                GUI.DrawLine(sb, center, center + new Vector2(flowForce.X, -flowForce.Y) / 10.0f, Color.Red);

                GUI.DrawLine(sb, center + Vector2.One * 5.0f, center + new Vector2(lerpedFlowForce.X, -lerpedFlowForce.Y) / 10.0f + Vector2.One * 5.0f, Color.Orange);
            }

            if (!editing || !ShowGaps) return;

            Color clr = (open == 0.0f) ? Color.Red : Color.Cyan;
            if (isHighlighted) clr = Color.Gold;

            float depth = (ID % 255) * 0.000001f;

            GUI.DrawRectangle(
                sb, new Rectangle(WorldRect.X, -WorldRect.Y, rect.Width, rect.Height),
                clr * 0.5f, true,
                depth,
                (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));

            for (int i = 0; i < linkedTo.Count; i++)
            {
                Vector2 dir = IsHorizontal ?
                    new Vector2(Math.Sign(linkedTo[i].Rect.Center.X - rect.Center.X), 0.0f)
                    : new Vector2(0.0f, Math.Sign((linkedTo[i].Rect.Y - linkedTo[i].Rect.Height / 2.0f) - (rect.Y - rect.Height / 2.0f)));

                Vector2 arrowPos = new Vector2(WorldRect.Center.X, -(WorldRect.Y - WorldRect.Height / 2));
                arrowPos += new Vector2(dir.X * (WorldRect.Width / 2 + 10), dir.Y * (WorldRect.Height / 2 + 10));

                GUI.Arrow.Draw(sb,
                    arrowPos, clr * 0.8f,
                    GUI.Arrow.Origin, MathUtils.VectorToAngle(dir) + MathHelper.PiOver2,
                    IsHorizontal ? new Vector2(rect.Height / 16.0f, 1.0f) : new Vector2(rect.Width / 16.0f, 1.0f),
                    SpriteEffects.None, depth);
            }

            if (IsSelected)
            {
                GUI.DrawRectangle(sb,
                    new Vector2(WorldRect.X - 5, -WorldRect.Y - 5),
                    new Vector2(rect.Width + 10, rect.Height + 10),
                    Color.Red,
                    false,
                    depth,
                    (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }
        }
    }
}
