using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Items.Components
{
    partial class Holdable : IDrawableComponent
    {
        public Vector2 DrawSize
        {
            get { return item.Rect.Size.ToVector2(); }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (!IsActive || picker == null || !CanBeAttached() || !picker.IsKeyDown(InputType.Aim)) { return; }

            Vector2 gridPos = picker.Position;
            Vector2 roundedGridPos = new Vector2(
                MathUtils.RoundTowardsClosest(picker.Position.X, Submarine.GridSize.X),
                MathUtils.RoundTowardsClosest(picker.Position.Y, Submarine.GridSize.Y));
            Vector2 attachPos = GetAttachPosition(picker);

            if (item.Submarine == null)
            {
                Structure attachTarget = Structure.GetAttachTarget(item.WorldPosition);
                if (attachTarget != null)
                {
                    if (attachTarget.Submarine != null)
                    {
                        //set to submarine-relative position
                        gridPos += attachTarget.Submarine.Position;
                        roundedGridPos += attachTarget.Submarine.Position;
                        attachPos += attachTarget.Submarine.Position;
                    }
                }
            }
            else
            {
                gridPos += item.Submarine.Position;
                roundedGridPos += item.Submarine.Position;
                attachPos += item.Submarine.Position;
            }

            Submarine.DrawGrid(spriteBatch, 14, gridPos, roundedGridPos);

            item.Sprite.Draw(
                spriteBatch,
                new Vector2(attachPos.X, -attachPos.Y),
                item.SpriteColor * 0.5f,
                0.0f, item.Scale, SpriteEffects.None, 0.0f);

            GUI.DrawRectangle(spriteBatch, new Vector2(attachPos.X - 2, -attachPos.Y - 2), Vector2.One * 5, Color.Red, thickness: 3);
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            if (!attachable || body == null) { return; }

            Vector2 attachPos = (Vector2)extraData[2];
            msg.Write(attachPos.X);
            msg.Write(attachPos.Y);
            Submarine parentSub = (Submarine)extraData[3];
            msg.Write(parentSub == null ? Entity.NullEntityID : parentSub.ID);
        }

        public override void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            base.ClientRead(type, msg, sendingTime);
            bool shouldBeAttached = msg.ReadBoolean();
            Vector2 simPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());

            if (!attachable)
            {
                DebugConsole.ThrowError("Received an attachment event for an item that's not attachable.");
                return;
            }

            if (shouldBeAttached)
            {
                if (!attached)
                {
                    Drop(false, null);
                    item.SetTransform(simPosition, 0.0f);
                    AttachToWall();
                }
            }
            else
            {
                if (attached)
                {
                    DropConnectedWires(null);

                    if (body != null)
                    {
                        item.body = body;
                        item.body.Enabled = true;
                    }
                    IsActive = false;

                    DeattachFromWall();
                }
            }
        }
    }
}
