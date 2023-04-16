using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics.Tracing;

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
            if (!IsActive || picker == null || !CanBeAttached(picker) || !picker.IsKeyDown(InputType.Aim) || picker != Character.Controlled)
            {
                Drawable = false;
                return;
            }

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

            Submarine.DrawGrid(spriteBatch, 14, gridPos, roundedGridPos, alpha: 0.4f);

            item.Sprite.Draw(
                spriteBatch,
                new Vector2(attachPos.X, -attachPos.Y),
                item.SpriteColor * 0.5f,
                0.0f, item.Scale, SpriteEffects.None, 0.0f);

            GUI.DrawRectangle(spriteBatch, new Vector2(attachPos.X - 2, -attachPos.Y - 2), Vector2.One * 5, GUIStyle.Red, thickness: 3);
        }

        public override bool ValidateEventData(NetEntityEvent.IData data)
            => TryExtractEventData<EventData>(data, out _);

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            if (!attachable || body == null) { return; }
            
            var eventData = ExtractEventData<EventData>(extraData);

            Vector2 attachPos = eventData.AttachPos;
            msg.WriteSingle(attachPos.X);
            msg.WriteSingle(attachPos.Y);
        }

        public override void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            base.ClientEventRead(msg, sendingTime);

            bool readAttachData = msg.ReadBoolean();
            if (!readAttachData) { return; }

            bool shouldBeAttached = msg.ReadBoolean();
            Vector2 simPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());
            UInt16 submarineID = msg.ReadUInt16();
            Submarine sub = Entity.FindEntityByID(submarineID) as Submarine;

            if (shouldBeAttached)
            {
                if (!attached)
                {
                    Drop(false, null);
                    item.SetTransform(simPosition, 0.0f);
                    item.Submarine = sub;
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
                else
                {
                    item.SetTransform(simPosition, 0.0f);
                    item.Submarine = sub;
                }
            }
        }
    }
}
